/**
 * Capacity / stress на проде (не smoke).
 *
 * setup() — фаза k6 ДО старта VU: выполняется 1 раз.
 * Там логинимся (admin + dept) и раздаём cookie всем VU.
 * Почему не логин в каждом VU: на API EnableRateLimiting("login") = 10/min/IP.
 * 100 VU × login = сразу 429, тест умрёт на auth, а не на API/БД.
 * Одна cookie на роль = нормально для поиска потолка RPS/latency/5xx.
 *
 * Вложения: опционально (UPLOAD_ATTACH=1). После теста файлы на диске
 * SQL не удалит — чисти каталог attachments (см. wipe-attachments.ps1).
 */
import http from 'k6/http';
import { check, sleep } from 'k6';
import { Rate, Trend, Counter } from 'k6/metrics';

const BASE = (__ENV.BASE_URL || '').replace(/\/$/, '');
const ADMIN_USER = __ENV.ADMIN_USER || 'admin';
const ADMIN_PASS = __ENV.ADMIN_PASS || '';
const DEPT_USER = __ENV.DEPT_USER || 'inic';
const DEPT_PASS = __ENV.DEPT_PASS || '';
const WRITE_RATE = Math.min(1, Math.max(0, Number(__ENV.WRITE_RATE || '0.35')));
const UPLOAD_ATTACH = (__ENV.UPLOAD_ATTACH || '0') === '1';
const ATTACH_EVERY_N = Math.max(1, Number(__ENV.ATTACH_EVERY_N || '5')); // каждый N-й create
const REPORT_DIR = (__ENV.REPORT_DIR || '/reports').replace(/\/$/, '');
/** Ступени целевого RPS сценария (для текста «на каком уровне сорвалось»). */
const RAMP_TARGETS = [30, 80, 150, 250, 400];

const errRate = new Rate('crown_errors');
const createOk = new Counter('crown_creates');
const attachOk = new Counter('crown_attaches');
const readMs = new Trend('crown_read_ms');
const writeMs = new Trend('crown_write_ms');

export const options = {
  insecureSkipTLSVerify: true,
  // Ищем предел: при росте ошибок/латентности — abort (смотрел лог: на каком target сломалось)
  thresholds: {
    http_req_failed: [
      { threshold: 'rate<0.15', abortOnFail: true, delayAbortEval: '30s' },
    ],
    crown_errors: [
      { threshold: 'rate<0.15', abortOnFail: true, delayAbortEval: '30s' },
    ],
    http_req_duration: [
      { threshold: 'p(95)<5000', abortOnFail: true, delayAbortEval: '30s' },
    ],
  },
  scenarios: {
    ramp: {
      executor: 'ramping-arrival-rate',
      startRate: 10,
      timeUnit: '1s',
      preAllocatedVUs: 80,
      maxVUs: Number(__ENV.MAX_VUS || '400'),
      stages: [
        { target: 30, duration: '1m' },
        { target: 80, duration: '2m' },
        { target: 150, duration: '2m' },
        { target: 250, duration: '2m' },
        { target: 400, duration: '2m' },
        { target: 0, duration: '30s' },
      ],
      exec: 'mixed',
    },
  },
};

function cookieHeader(res) {
  const parts = [];
  for (const [name, list] of Object.entries(res.cookies || {})) {
    if (list && list.length > 0 && list[0].value) {
      parts.push(`${name}=${list[0].value}`);
    }
  }
  return parts.join('; ');
}

function login(user, pass) {
  const res = http.post(
    `${BASE}/api/auth/login`,
    JSON.stringify({ userName: user, password: pass }),
    {
      headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
      tags: { name: 'login' },
    },
  );
  if (res.status !== 200) {
    throw new Error(`login ${user}: ${res.status} ${String(res.body).slice(0, 200)}`);
  }
  return cookieHeader(res);
}

function authGet(path, cookie, tag) {
  const t0 = Date.now();
  const res = http.get(`${BASE}${path}`, {
    headers: { Cookie: cookie, Accept: 'application/json' },
    tags: { name: tag },
  });
  readMs.add(Date.now() - t0);
  const ok = res.status >= 200 && res.status < 300;
  errRate.add(!ok);
  check(res, { [`${tag} ok`]: () => ok });
  return res;
}

export function setup() {
  if (!BASE) throw new Error('BASE_URL required');
  if (!ADMIN_PASS || !DEPT_PASS) throw new Error('ADMIN_PASS and DEPT_PASS required');

  const h = http.get(`${BASE}/health`);
  if (h.status !== 200) throw new Error(`health ${h.status}`);

  // Ровно 2 логина на весь прогон (setup = один раз до VU).
  const adminCookie = login(ADMIN_USER, ADMIN_PASS);
  sleep(0.5);
  const deptCookie = login(DEPT_USER, DEPT_PASS);

  const assignees = http.get(`${BASE}/api/requests/assignees`, {
    headers: { Cookie: deptCookie, Accept: 'application/json' },
  });
  let executorUserId = null;
  if (assignees.status === 200) {
    const list = assignees.json();
    if (Array.isArray(list) && list.length > 0) {
      executorUserId = list[0].id || list[0].Id;
    }
  }
  if (!executorUserId) {
    throw new Error('нет assignees — create заявок не взлетит');
  }

  // маленький «файл» для upload (в памяти)
  const attachBytes = 'x'.repeat(64 * 1024); // 64 KiB

  return { adminCookie, deptCookie, executorUserId, attachBytes };
}

export function mixed(data) {
  const roll = Math.random();

  // 50% dept read / 25% admin read / 25%*WRITE_RATE create (иначе ещё read)
  if (roll < 0.5) {
    authGet('/api/requests', data.deptCookie, 'dept_requests');
    if (Math.random() < 0.5) authGet('/api/requests/today', data.deptCookie, 'dept_today');
    if (Math.random() < 0.4) authGet('/api/equipment', data.deptCookie, 'dept_equipment');
    if (Math.random() < 0.3) authGet('/api/special-vehicles', data.deptCookie, 'dept_spectech');
    if (Math.random() < 0.2) authGet('/api/auth/me', data.deptCookie, 'dept_me');
    return;
  }

  if (roll < 0.75) {
    authGet('/api/requests', data.adminCookie, 'admin_requests');
    if (Math.random() < 0.5) authGet('/api/users', data.adminCookie, 'admin_users');
    if (Math.random() < 0.4) authGet('/api/transport/board', data.adminCookie, 'admin_transport');
    if (Math.random() < 0.3) authGet('/api/summary', data.adminCookie, 'admin_summary');
    if (Math.random() < 0.25) authGet('/api/audit-log?take=50', data.adminCookie, 'admin_audit');
    return;
  }

  if (Math.random() < WRITE_RATE) {
    createRequest(data);
    return;
  }

  const res = http.get(`${BASE}/health`, { tags: { name: 'health' } });
  errRate.add(res.status !== 200);
}

function createRequest(data) {
  const due = new Date(Date.now() + 86400000).toISOString();
  const t0 = Date.now();
  const res = http.post(
    `${BASE}/api/requests`,
    JSON.stringify({
      phone: '+70000000000',
      dueAt: due,
      executorUserId: data.executorUserId,
      body: `stress ${__VU}-${__ITER}-${Date.now()}`,
      kind: 'Other',
      isUrgent: Math.random() < 0.1,
    }),
    {
      headers: {
        Cookie: data.deptCookie,
        'Content-Type': 'application/json',
        Accept: 'application/json',
      },
      tags: { name: 'create_request' },
    },
  );
  writeMs.add(Date.now() - t0);
  const ok = res.status >= 200 && res.status < 300;
  errRate.add(!ok);
  check(res, { 'create 2xx': () => ok });
  if (!ok) return;

  createOk.add(1);
  let id = null;
  try {
    const body = res.json();
    id = body.id || body.Id;
  } catch (_) {
    return;
  }

  if (!UPLOAD_ATTACH || !id || __ITER % ATTACH_EVERY_N !== 0) return;

  const form = {
    file: http.file(data.attachBytes, `stress-${__VU}-${__ITER}.txt`, 'text/plain'),
  };
  const up = http.post(`${BASE}/api/requests/${id}/attachments`, form, {
    headers: { Cookie: data.deptCookie, Accept: 'application/json' },
    tags: { name: 'upload_attach' },
  });
  const upOk = up.status >= 200 && up.status < 300;
  errRate.add(!upOk);
  if (upOk) attachOk.add(1);
}

// ---------- отчёт ----------

function mval(data, name, key, fallback) {
  const m = data.metrics && data.metrics[name];
  if (!m || !m.values) return fallback;
  const v = m.values[key];
  return typeof v === 'number' && !Number.isNaN(v) ? v : fallback;
}

function round(n, digits) {
  if (n === null || n === undefined || Number.isNaN(n)) return null;
  const p = 10 ** (digits || 0);
  return Math.round(n * p) / p;
}

function fmtMs(ms) {
  if (ms === null || ms === undefined) return 'н/д';
  if (ms < 1000) return `${round(ms, 0)} мс`;
  return `${round(ms / 1000, 2)} с`;
}

function fmtNum(n) {
  if (n === null || n === undefined) return 'н/д';
  return String(round(n, n >= 100 ? 0 : 1));
}

function thresholdFailed(data) {
  const t = data.thresholds || {};
  const failed = [];
  for (const [name, info] of Object.entries(t)) {
    if (info && info.ok === false) failed.push(name);
  }
  return failed;
}

function estimateUsers(avgRps) {
  // Грубая модель «активный пользователь в UI»:
  // спокойный клик/обновление списка ~1 запрос / 5–10 с → 0.1–0.2 rps на человека
  // активная работа (фильтры, карточки) ~1 / 2–3 с → ~0.3–0.5 rps
  if (!avgRps || avgRps <= 0) {
    return { calm: null, active: null, note: 'нет данных по RPS' };
  }
  return {
    calmMin: Math.floor(avgRps / 0.2),
    calmMax: Math.floor(avgRps / 0.1),
    activeMin: Math.floor(avgRps / 0.5),
    activeMax: Math.floor(avgRps / 0.3),
  };
}

function nearestRampTarget(avgRps) {
  if (!avgRps) return null;
  let best = RAMP_TARGETS[0];
  let bestDiff = Math.abs(avgRps - best);
  for (const t of RAMP_TARGETS) {
    const d = Math.abs(avgRps - t);
    if (d < bestDiff) {
      best = t;
      bestDiff = d;
    }
  }
  return best;
}

function buildReport(data) {
  const durMs = data.state && data.state.testRunDurationMs ? data.state.testRunDurationMs : 0;
  const durSec = durMs / 1000;
  const totalReqs = mval(data, 'http_reqs', 'count', 0);
  const avgRps = mval(data, 'http_reqs', 'rate', durSec > 0 ? totalReqs / durSec : 0);
  const failRate = mval(data, 'http_req_failed', 'rate', 0);
  const errCustom = mval(data, 'crown_errors', 'rate', 0);
  const p50 = mval(data, 'http_req_duration', 'med', null);
  const p95 = mval(data, 'http_req_duration', 'p(95)', null);
  const p99 = mval(data, 'http_req_duration', 'p(99)', null);
  const maxLat = mval(data, 'http_req_duration', 'max', null);
  const creates = mval(data, 'crown_creates', 'count', 0);
  const attaches = mval(data, 'crown_attaches', 'count', 0);
  const vusMax = mval(data, 'vus_max', 'max', mval(data, 'vus_max', 'value', null));
  const checksRate = mval(data, 'checks', 'rate', null);
  const failedTh = thresholdFailed(data);
  const aborted = failedTh.length > 0;
  const users = estimateUsers(avgRps);
  const rampNear = nearestRampTarget(avgRps);

  let verdict;
  if (aborted) {
    verdict =
      `Тест оборван порогом (ошибки/задержки). Средний поток за прогон ~${fmtNum(avgRps)} req/s ` +
      `(рядом со ступенью ~${rampNear || '?'} req/s). Это ориентир потолка на текущем железе.`;
  } else {
    verdict =
      `Тест дошёл до конца без срабатывания abort. Система выдержала сценарий до ${RAMP_TARGETS[RAMP_TARGETS.length - 1]} req/s ` +
      `(факт. средний поток ~${fmtNum(avgRps)} req/s). Потолок выше — можно поднять MAX_VUS / ступени.`;
  }

  const execLines = [
    '════════════════════════════════════════════════════════',
    '  ИТОГОВЫЕ РЕЗУЛЬТАТЫ (на пальцах)',
    '════════════════════════════════════════════════════════',
    '',
    `Цель теста: ${BASE || 'н/д'}`,
    `Длительность прогона: ${fmtNum(durSec)} с`,
    '',
    `• Сколько запросов система переварила: ${fmtNum(totalReqs)} шт.`,
    `• Средняя скорость: ~${fmtNum(avgRps)} запросов в секунду`,
    `• Доля неуспешных HTTP: ${round(failRate * 100, 2)}%`,
    `• Задержки ответа: типичная (медиана) ${fmtMs(p50)}, у 95% пользователей быстрее ${fmtMs(p95)}, редкий хвост (99%) ${fmtMs(p99)}, максимум ${fmtMs(maxLat)}`,
    `• Создано заявок за тест: ${fmtNum(creates)}` +
      (UPLOAD_ATTACH ? `, вложений залито: ${fmtNum(attaches)}` : ' (загрузка файлов выключена)'),
    '',
    'Ориентир по одновременным ПОЛЬЗОВАТЕЛЯМ (очень грубо, не лицензия):',
    users.calmMin != null
      ? `  — спокойная работа в UI: примерно ${users.calmMin}–${users.calmMax} человек одновременно`
      : '  — н/д',
    users.activeMin != null
      ? `  — активная работа (много кликов/списков): примерно ${users.activeMin}–${users.activeMax} человек`
      : '',
    '  (модель: спокойный пользователь ~0.1–0.2 req/s, активный ~0.3–0.5 req/s)',
    '',
    `Вердикт: ${verdict}`,
    '',
    'Оговорки: тест гонялся с 2 учёток (admin + подразделение) из‑за лимита логинов;',
    'цифры — про текущее железо/сеть/БД на момент прогона, не гарантия на годы.',
    '════════════════════════════════════════════════════════',
  ];

  const techLines = [
    '# Crown stress report',
    '',
    `Generated: ${new Date().toISOString()}`,
    `Base URL: ${BASE}`,
    `WriteRate: ${WRITE_RATE}, UploadAttach: ${UPLOAD_ATTACH}, MaxVUs env: ${__ENV.MAX_VUS || '400'}`,
    '',
    '## Executive summary',
    '',
    '```',
    ...execLines,
    '```',
    '',
    '## Technical',
    '',
    `| Metric | Value |`,
    `| --- | --- |`,
    `| duration_s | ${round(durSec, 1)} |`,
    `| http_reqs | ${totalReqs} |`,
    `| avg_rps | ${round(avgRps, 2)} |`,
    `| http_req_failed_rate | ${round(failRate, 4)} |`,
    `| crown_errors_rate | ${round(errCustom, 4)} |`,
    `| latency_med_ms | ${round(p50, 1)} |`,
    `| latency_p95_ms | ${round(p95, 1)} |`,
    `| latency_p99_ms | ${round(p99, 1)} |`,
    `| latency_max_ms | ${round(maxLat, 1)} |`,
    `| creates | ${creates} |`,
    `| attaches | ${attaches} |`,
    `| vus_max | ${vusMax} |`,
    `| checks_rate | ${checksRate} |`,
    `| thresholds_failed | ${failedTh.join(', ') || 'none'} |`,
    '',
    '## Thresholds raw',
    '',
    '```json',
    JSON.stringify(data.thresholds || {}, null, 2),
    '```',
  ];

  const payload = {
    generatedAt: new Date().toISOString(),
    baseUrl: BASE,
    writeRate: WRITE_RATE,
    uploadAttach: UPLOAD_ATTACH,
    durationSec: round(durSec, 1),
    totalRequests: totalReqs,
    avgRps: round(avgRps, 2),
    httpFailedRate: round(failRate, 4),
    crownErrorRate: round(errCustom, 4),
    latencyMs: { med: round(p50, 1), p95: round(p95, 1), p99: round(p99, 1), max: round(maxLat, 1) },
    creates,
    attaches,
    vusMax,
    thresholdsFailed: failedTh,
    aborted,
    estimatedConcurrentUsers: users,
    nearestRampTargetReqPerSec: rampNear,
    verdict,
  };

  return {
    executiveText: execLines.join('\n'),
    markdown: techLines.join('\n') + '\n',
    payload,
  };
}

export function handleSummary(data) {
  const report = buildReport(data);
  const stamp = new Date().toISOString().replace(/[:.]/g, '-');
  const out = {
    stdout: `\n${report.executiveText}\n\n(полные файлы: ${REPORT_DIR}/latest.md , ${REPORT_DIR}/latest.json)\n`,
  };
  out[`${REPORT_DIR}/latest.md`] = report.markdown;
  out[`${REPORT_DIR}/latest.json`] = JSON.stringify(report.payload, null, 2);
  out[`${REPORT_DIR}/latest-exec.txt`] = `${report.executiveText}\n`;
  out[`${REPORT_DIR}/stress-${stamp}.md`] = report.markdown;
  out[`${REPORT_DIR}/stress-${stamp}.json`] = JSON.stringify(report.payload, null, 2);
  out[`${REPORT_DIR}/stress-${stamp}-exec.txt`] = `${report.executiveText}\n`;
  return out;
}
