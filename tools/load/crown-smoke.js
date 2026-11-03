/**
 * Локальный smoke под нагрузкой (не capacity-тест).
 * - health без auth
 * - admin/dept: списки hot paths с cookie после одного login в setup
 * - редкий POST заявки (Other) — чтобы не засыпать БД
 *
 * Login rate-limit = 10/min/IP → логинимся только в setup().
 */
import http from 'k6/http';
import { check, sleep, group } from 'k6';
import { Rate, Trend } from 'k6/metrics';

const BASE = (__ENV.BASE_URL || 'http://api:8080').replace(/\/$/, '');
const ADMIN_USER = __ENV.ADMIN_USER || 'admin';
const ADMIN_PASS = __ENV.ADMIN_PASS || '';
const DEPT_USER = __ENV.DEPT_USER || 'inic';
const DEPT_PASS = __ENV.DEPT_PASS || '';
/** Доля итераций dept с POST /api/requests (0..1). На проде до ввода можно поднять, напр. 0.2 */
const WRITE_RATE = Math.min(1, Math.max(0, Number(__ENV.WRITE_RATE || '0.05')));

const errRate = new Rate('crown_errors');
const loginMs = new Trend('crown_login_ms');

export const options = {
  // самоподписанный Caddy/внутренний HTTPS
  insecureSkipTLSVerify: true,
  scenarios: {
    health: {
      executor: 'constant-vus',
      vus: 5,
      duration: '45s',
      exec: 'health',
      startTime: '0s',
    },
    dept_reads: {
      executor: 'ramping-vus',
      startVUs: 0,
      stages: [
        { duration: '10s', target: 10 },
        { duration: '40s', target: 20 },
        { duration: '10s', target: 0 },
      ],
      exec: 'deptReads',
      startTime: '2s',
    },
    admin_reads: {
      executor: 'ramping-vus',
      startVUs: 0,
      stages: [
        { duration: '10s', target: 5 },
        { duration: '40s', target: 10 },
        { duration: '10s', target: 0 },
      ],
      exec: 'adminReads',
      startTime: '2s',
    },
  },
  thresholds: {
    http_req_failed: ['rate<0.05'],
    crown_errors: ['rate<0.05'],
    http_req_duration: ['p(95)<2000'],
  },
};

function cookieHeader(res) {
  // k6: res.cookies[name] = [{ name, value, ... }, ...]
  const parts = [];
  for (const [name, list] of Object.entries(res.cookies || {})) {
    if (list && list.length > 0 && list[0].value) {
      parts.push(`${name}=${list[0].value}`);
    }
  }
  return parts.join('; ');
}

function login(user, pass) {
  const started = Date.now();
  const res = http.post(
    `${BASE}/api/auth/login`,
    JSON.stringify({ userName: user, password: pass }),
    {
      headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
      tags: { name: 'login' },
    },
  );
  loginMs.add(Date.now() - started);
  const ok = check(res, {
    'login 200': (r) => r.status === 200,
  });
  errRate.add(!ok);
  if (!ok) {
    throw new Error(`login failed for ${user}: ${res.status} ${res.body}`);
  }
  return cookieHeader(res);
}

function authGet(path, cookie, tag) {
  const res = http.get(`${BASE}${path}`, {
    headers: { Cookie: cookie, Accept: 'application/json' },
    tags: { name: tag },
  });
  const ok = check(res, {
    [`${tag} 2xx`]: (r) => r.status >= 200 && r.status < 300,
  });
  errRate.add(!ok);
  return res;
}

export function setup() {
  if (!ADMIN_PASS || !DEPT_PASS) {
    throw new Error('ADMIN_PASS and DEPT_PASS env required');
  }
  // health probe
  const h = http.get(`${BASE}/health`);
  if (h.status !== 200) {
    throw new Error(`API health ${h.status}`);
  }
  const adminCookie = login(ADMIN_USER, ADMIN_PASS);
  sleep(0.3);
  const deptCookie = login(DEPT_USER, DEPT_PASS);

  // исполнитель для редкого create — любой assignee
  const assignees = http.get(`${BASE}/api/requests/assignees`, {
    headers: { Cookie: deptCookie, Accept: 'application/json' },
  });
  let executorUserId = null;
  if (assignees.status === 200) {
    try {
      const list = assignees.json();
      if (Array.isArray(list) && list.length > 0) {
        executorUserId = list[0].id || list[0].Id;
      }
    } catch (_) {
      /* ignore */
    }
  }

  return { adminCookie, deptCookie, executorUserId };
}

export function health() {
  const res = http.get(`${BASE}/health`, { tags: { name: 'health' } });
  const ok = check(res, { 'health 200': (r) => r.status === 200 });
  errRate.add(!ok);
  sleep(0.2);
}

export function deptReads(data) {
  group('dept', () => {
    authGet('/api/auth/me', data.deptCookie, 'dept_me');
    authGet('/api/requests', data.deptCookie, 'dept_requests');
    authGet('/api/requests/today', data.deptCookie, 'dept_requests_today');
    authGet('/api/equipment', data.deptCookie, 'dept_equipment');
    authGet('/api/special-vehicles', data.deptCookie, 'dept_spectech');

    if (data.executorUserId && Math.random() < WRITE_RATE) {
      const due = new Date(Date.now() + 86400000).toISOString();
      const res = http.post(
        `${BASE}/api/requests`,
        JSON.stringify({
          phone: '+70000000000',
          dueAt: due,
          executorUserId: data.executorUserId,
          body: `load-smoke ${__VU}-${__ITER}`,
          kind: 'Other',
          isUrgent: false,
        }),
        {
          headers: {
            Cookie: data.deptCookie,
            'Content-Type': 'application/json',
            Accept: 'application/json',
          },
          tags: { name: 'dept_create_request' },
        },
      );
      const ok = check(res, {
        'create 2xx/4xx-not-5xx': (r) => r.status < 500,
      });
      errRate.add(!ok || res.status >= 500);
    }
  });
  sleep(0.3);
}

export function adminReads(data) {
  group('admin', () => {
    authGet('/api/auth/me', data.adminCookie, 'admin_me');
    authGet('/api/requests', data.adminCookie, 'admin_requests');
    authGet('/api/users', data.adminCookie, 'admin_users');
    authGet('/api/transport/board', data.adminCookie, 'admin_transport');
    authGet('/api/audit-log?take=50', data.adminCookie, 'admin_audit');
    authGet('/api/summary', data.adminCookie, 'admin_summary');
  });
  sleep(0.3);
}
