using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

QuestPDF.Settings.License = LicenseType.Community;

var outDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docs"));
if (!Directory.Exists(outDir))
{
    // fallback: relative to cwd when running from tools/ForkBriefPdf
    outDir = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "docs"));
}

Directory.CreateDirectory(outDir);
var outPath = Path.Combine(outDir, "Ratchet-Fork-Brief-WellRepair.pdf");

var accent = Color.FromHex("#1B4F72");
var muted = Color.FromHex("#5D6D7E");
var line = Color.FromHex("#D5D8DC");
var keepBg = Color.FromHex("#E8F5E9");
var dropBg = Color.FromHex("#FDEDEC");
var warnBg = Color.FromHex("#FEF9E7");

Document.Create(container =>
{
    container.Page(page =>
    {
        page.Size(PageSizes.A4);
        page.MarginVertical(40);
        page.MarginHorizontal(48);
        page.DefaultTextStyle(x => x.FontSize(10).FontColor(Colors.Grey.Darken3).FontFamily("Arial"));

        page.Header().Column(col =>
        {
            col.Item().Text("WWS Ratchet → форк под заявки ремонта скважин")
                .FontSize(16).Bold().FontColor(accent);
            col.Item().Text("Бриф для отдельного агента / отдельного репозитория")
                .FontSize(10).FontColor(muted);
            col.Item().PaddingTop(4).Text($"Версия источника: 1.0.1.4 · Дата: {DateTime.Now:yyyy-MM-dd}")
                .FontSize(9).FontColor(muted);
            col.Item().PaddingTop(8).LineHorizontal(1).LineColor(line);
        });

        page.Footer().AlignCenter().Text(t =>
        {
            t.Span("конфиденциально · для внутреннего агента · стр. ").FontSize(8).FontColor(muted);
            t.CurrentPageNumber().FontSize(8).FontColor(muted);
            t.Span(" / ").FontSize(8).FontColor(muted);
            t.TotalPages().FontSize(8).FontColor(muted);
        });

        page.Content().PaddingTop(16).Column(main =>
        {
            main.Spacing(12);

            Section(main, "1. Назначение документа",
                "Этот PDF — входной контекст для нового агента, который создаёт отдельный проект (форк/наследник), " +
                "а не продолжает доработку CRM автосервиса. Источник: репозиторий WerkonWebServicesRatchet (WWS Ratchet). " +
                "Цель форка: заявочная система ремонта скважин в нефтяной промышленности, on-prem у клиента, доступ через мобильный интернет, ~50 одновременных пользователей.");

            Section(main, "2. Что такое Ratchet сейчас",
                "CRM / операционка для небольшой автомастерской (MVP под ежедневную работу). " +
                "Домен: клиенты, автомобили, сделки/визиты, воронка заявок (inquiries), календарь слотов по боксам/постам и механикам, " +
                "каталог услуг/запчастей, склад, оплаты, напоминания, PDF (заказ-наряд, смета, отчёты), журнал аудита, " +
                "системный статус, бэкапы (в т.ч. restic), in-app обновление стека.");

            main.Item().Element(e => Card(e, "Стек",
                "• Backend: ASP.NET Core Web API, net10.0, EF Core, PostgreSQL 17, Identity (cookie)\n" +
                "• Frontend: Blazor Web App, Interactive Server (SignalR circuit)\n" +
                "• PDF: QuestPDF\n" +
                "• Deploy: Docker Compose — postgres, api, web, Caddy, backup\n" +
                "• Локализация: ru / en / ja; валюта и язык — host-managed\n" +
                "• Роли: Administrator, Manager, Mechanic, MechanicManager"));

            Section(main, "3. Целевой продукт (форк)",
                "Заявочная система ремонта скважин: диспетчеризация и сопровождение заявок/нарядов на ремонт в нефтепромысловом контуре. " +
                "Развёртывание на сервере предприятия заказчика. Пользователи — офис + поле (мастера/подрядчики) через мобильный интернет. " +
                "Нагрузка порядка 50 одновременных сессий — по CPU/RAM стек достаточен; узкое место — сеть и модель UI, не «50 человек».");

            Section(main, "4. Вердикт по пригодности стека",
                "Инфраструктурный каркас on-prem (Docker, Postgres, Identity, audit, backups, update) — подходит. " +
                "Домен автосервиса переносить нельзя: получится Франкенштейн. " +
                "Главный риск: Blazor Server + нестабильный LTE в поле (обрывы SignalR → «мёртвые» кнопки, залипания после скачиваний). " +
                "Рекомендация: форк инфраструктуры + новый bounded context; для поля — WASM/PWA или лёгкий клиент к тому же API; офис может остаться на Blazor Server.");

            main.Item().Text("5. Что переиспользовать (KEEP)").FontSize(12).Bold().FontColor(accent);
            main.Item().Background(keepBg).Padding(8).Text(
                "• Docker Compose / Caddy / healthchecks / .env-паттерн\n" +
                "• Identity, политики авторизации, управление пользователями, audit log\n" +
                "• Паттерны статусов/этапов, soft-archive, пагинация/infinite scroll\n" +
                "• Backup (dump + restic), system status, update-stack (адаптировать под закрытый контур)\n" +
                "• QuestPDF-пайплайн, организация/логотип в документах\n" +
                "• DataProtection keys volume, локализация host-level\n" +
                "• API + отдельный Web-проект, cookie-сессии через reverse proxy")
                .FontSize(9);

            main.Item().Text("6. Что вырезать / не тащить (DROP)").FontSize(12).Bold().FontColor(accent);
            main.Item().Background(dropBg).Padding(8).Text(
                "• Календарь боксов/постов автосервиса, multi-slot visit calendar как есть\n" +
                "• Склад автозапчастей и каталоги услуг СТО (модель другая)\n" +
                "• Оплаты/светофор оплаты, PDF заказ-наряда автосервиса «как есть»\n" +
                "• Unattached visits / привязка к VIN/госномеру без переосмысления\n" +
                "• Inquiries kanban авто-CRM без смены сущностей (скважина ≠ клиент СТО)\n" +
                "• Любые «переименования Visit → WellJob» без новой схемы БД")
                .FontSize(9);

            main.Item().Text("7. Рекомендуемый домен нового продукта").FontSize(12).Bold().FontColor(accent);
            main.Item().PaddingLeft(6).Text(
                "Скважина / объект → Заявка → Наряд / работы → Исполнители (бригада/подрядчик) → " +
                "Акты / вложения (фото) → Закрытие / отчётность. " +
                "Отдельно: месторождения/кусты, справочник скважин, типы ремонтов, приоритеты, SLA, " +
                "видимость по оргструктуре (не все видят все объекты).")
                .FontSize(9);

            main.Item().Text("8. On-prem и безопасность").FontSize(12).Bold().FontColor(accent);
            Bullet(main,
                "Закрытый контур: без GitHub pull; доставка обновлений офлайн-пакетами образов",
                "TLS даже «внутри периметра»; свои CA / внутренний DNS",
                "Часто потребуют AD/LDAP — заложить адаптер аутентификации",
                "Секреты только в .env / secret store хоста, не в образе",
                "Бэкапы на СХД заказчика + регулярный restore-тест",
                "Роли с scope по объектам/бригадам, не только глобальный Admin/Manager");

            main.Item().Text("9. Мобильный интернет и UI").FontSize(12).Bold().FontColor(accent);
            main.Item().Background(warnBg).Padding(8).Text(
                "Blazor Server держит SignalR-circuit: на LTE с обрывами UX деградирует. " +
                "Для полевых сценариев заложить: reconnect, короткие запросы, офлайн-черновики заявок, " +
                "крупные кнопки, минимум полей на экран, фото с камеры, поиск скважины по коду. " +
                "Офисная «толстая» Blazor-таблица ≠ мобильная витрина мастера.")
                .FontSize(9);

            main.Item().Text("10. Нагрузка ~50 одновременных").FontSize(12).Bold().FontColor(accent);
            main.Item().Text(
                "На VM 4–8 vCPU / 8–16 GB стек postgres+api+web обычно достаточен. " +
                "Упираться будут отчёты, вложения, конфликты одновременного редактирования одной заявки, " +
                "а не количество логинов. Заложить optimistic concurrency / явную блокировку карточки.")
                .FontSize(9);

            main.Item().Text("11. Инструкции новому агенту (как работать)").FontSize(12).Bold().FontColor(accent);
            Bullet(main,
                "Создать ОТДЕЛЬНЫЙ репозиторий (не продолжать feature-ветки Ratchet)",
                "Скопировать/форкнуть инфраструктурный скелет (compose, Identity, backup, update), затем вычистить домен СТО",
                "Спроектировать схему БД с нуля под скважины/заявки; миграции — свои",
                "API-first: контракты под поле и офис; UI офиса и поля можно развести",
                "Не обещать «просто перекрасим Ratchet» — явно разделить KEEP vs DROP",
                "Документировать on-prem install, backup/restore, офлайн-обновление",
                "Язык UI по умолчанию ru; учесть промышленный жаргон заказчика в словаре");

            main.Item().Text("12. Структура исходного репо (ориентир)").FontSize(12).Bold().FontColor(accent);
            main.Item().PaddingLeft(6).Text(
                "WerkonWebServicesRatchet — API\n" +
                "WerkonWebServicesRatchet.Web — Blazor UI\n" +
                "docker-compose.yml, docker/backup, scripts/update-stack.sh\n" +
                "README.md — актуальный перечень фич и ролей")
                .FontSize(9).FontFamily("Consolas");

            main.Item().PaddingTop(8).LineHorizontal(1).LineColor(line);
            main.Item().Text(
                "Итог: использовать Ratchet как проверенный on-prem каркас и набор operational-паттернов; " +
                "продукт строить как новый домен. Отдельный агент = отдельный репозиторий, отдельная схема, отдельный UI-контракт для поля.")
                .FontSize(9).Italic();
        });
    });
}).GeneratePdf(outPath);

Console.WriteLine(outPath);

static void Section(ColumnDescriptor main, string title, string body)
{
    main.Item().Text(title).FontSize(12).Bold().FontColor(Color.FromHex("#1B4F72"));
    main.Item().Text(body).FontSize(9);
}

static void Card(IContainer e, string title, string body)
{
    e.Border(1).BorderColor(Color.FromHex("#D5D8DC")).Padding(8).Column(c =>
    {
        c.Item().Text(title).Bold().FontSize(10).FontColor(Color.FromHex("#1B4F72"));
        c.Item().PaddingTop(4).Text(body).FontSize(9);
    });
}

static void Bullet(ColumnDescriptor main, params string[] items)
{
    foreach (var item in items)
    {
        main.Item().PaddingLeft(6).Text("• " + item).FontSize(9);
    }
}
