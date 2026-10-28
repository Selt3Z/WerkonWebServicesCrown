# AGENTS.md

## Project intent

This project is a pragmatic backend-first CRM/MVP for a small auto service business.

Primary goals:
1. Be genuinely useful for a real small auto service.
2. Be a strong portfolio project for backend interviews.
3. Stay simple, maintainable, and realistic.
4. Favor a finished, coherent MVP over broad but unfinished functionality.

This is **not** a generic CRM platform for all businesses.
This is **not** a microservices showcase.
This is **not** an overengineered enterprise template.

The project should solve a concrete workflow:
- store clients
- store their vehicles
- record visits and completed services
- keep service history
- let staff manually create maintenance reminders when needed
- show staff what follow-up is due

---

## Product scope

Build for **one auto service first**, but keep the design clean enough to later adapt to similar services.

Do not build:
- multi-tenant SaaS
- marketplace logic
- online booking platform
- accounting system
- warehouse/inventory system
- telephony integration
- messaging platform
- analytics platform
- document generation suite

Only add something outside MVP when explicitly requested.

---

## MVP features

The MVP should include:

1. Clients
- create
- edit
- list
- search by name and phone
- notes

2. Vehicles
- linked to a client
- brand
- model
- year
- license plate
- VIN optional
- do **not** store current mileage on Vehicle; mileage is known only at visit time and is stored on Visit as `MileageAtVisit`
- when latest mileage is needed (e.g. reminder comparison), derive it from the most recent visit for that vehicle

3. Visits
- create visit for a vehicle
- visit date
- mileage at visit
- client complaint/request
- mechanic comment
- status: Created / InProgress / Completed / Cancelled

4. Services performed during a visit
- select from service catalog
- allow custom manual service line
- quantity / price / comment where appropriate
- total cost for visit

5. Maintenance reminders
- created **manually by staff** after a visit when needed (not auto-generated on visit completion)
- different vehicles need different replacement intervals for parts, fluids, and services; staff decides what to remind and when
- reminder type or free-text description
- due by date and/or mileage (mileage compared against latest `MileageAtVisit` from visit history)
- statuses such as Pending / Due / Sent / Cancelled
- **no background worker**; evaluate whether a reminder is due when listing or querying reminders (compare due date to today and due mileage to latest visit mileage)

6. Minimal dashboard
- today visits
- active reminders
- recent visits
- recent clients

7. Authentication and authorization
- login
- at least Admin and Employee roles

8. Audit basics
- created at / updated at
- created by / updated by where reasonable

---

## Tech stack

Required stack:
- ASP.NET Core
- PostgreSQL
- Entity Framework Core
- EF Core Migrations
- Docker Compose

Do **not** add a background worker or separate worker service for MVP.

UI:
- Prefer minimal Blazor-based internal admin UI unless explicitly requested otherwise.
- UI is secondary to backend quality.
- Keep UI simple and functional.

Testing:
- xUnit for tests
- add tests for core business rules and critical application logic

---

## Architecture style

Use a **modular monolith**.

Preferred project structure:

- `src/Api` - HTTP API
- `src/Application` - use cases, DTOs, validation, business orchestration
- `src/Domain` - entities, enums, value objects, domain rules
- `src/Infrastructure` - EF Core, database access, auth, implementations
- `src/Web` - optional minimal Blazor UI
- `tests/...` - test projects

Alternative folder structure is acceptable if it remains clean and explicit.

Important:
- Keep boundaries clear.
- Domain should not depend on Infrastructure.
- Avoid unnecessary abstraction layers.
- Avoid “clean architecture theater”.

---

## Domain modeling rules

Model the real business explicitly.

Core entities are likely:
- Client
- Vehicle
- Visit
- VisitServiceItem
- ServiceCatalogItem
- MaintenanceReminder
- User

Possible value objects:
- PhoneNumber
- Money
- Mileage
- LicensePlate
- Vin

Use value objects only where they add clarity.
Do not create value objects for everything.

Reflect real relationships:
- one client can have many vehicles
- one vehicle can have many visits
- one visit can have many performed service items
- one vehicle can have many reminders; reminders are created manually by staff and are not auto-created from visits

Prefer explicit domain names over generic names like `Record`, `Item`, `Data`, `Manager`.

---

## Coding principles

Write production-style code, not tutorial code.

Requirements:
- use clear naming
- keep methods focused
- keep classes reasonably small
- prefer explicitness over magic
- handle nullability correctly
- use async appropriately
- add cancellation tokens where meaningful
- return structured errors from API
- validate input properly

Do not:
- add generic repository pattern just for tradition
- add unnecessary unit of work abstraction on top of EF Core
- add MediatR unless explicitly requested
- add CQRS split unless the task clearly benefits from it
- add AutoMapper unless explicitly requested
- add base classes everywhere
- add speculative abstractions for future features
- add event bus, message broker, or distributed architecture without explicit instruction

EF Core already is enough for most persistence needs in this project.

---

## API rules

Use REST-style HTTP APIs.

Guidelines:
- use clear route names
- do not leak EF entities directly from controllers
- use DTOs for request/response models
- keep controllers thin
- move business logic into Application layer
- use proper HTTP status codes
- return validation errors clearly
- support filtering/search where needed for MVP
- pagination is recommended for list endpoints if the list can grow

Possible endpoint groups:
- `/api/clients`
- `/api/vehicles`
- `/api/visits`
- `/api/services`
- `/api/reminders`
- `/api/auth`

Do not create dozens of endpoints for hypothetical future use cases.

---

## Database rules

Use PostgreSQL as the primary database.

Requirements:
- use EF Core migrations for schema evolution
- every schema change must be represented by a migration
- keep table/column names consistent
- configure constraints explicitly
- configure indexes for realistic searches
- use foreign keys
- choose delete behavior deliberately
- seed only minimal required reference data

Important:
- never store secrets in source code
- never commit production credentials
- keep connection strings in environment variables or proper local secrets

Likely indexes:
- client phone
- vehicle license plate
- vehicle VIN
- visit date
- reminder status + due date

Do not overdesign the schema for multitenancy from day one.
It is acceptable to leave room for future extension without implementing it now.

---

## Maintenance reminder rules

Reminders are a lightweight follow-up list, not an automated scheduling system.

Requirements:
- staff creates reminders manually from the UI when a specific client/vehicle needs future follow-up
- support due by date, due by mileage, or both
- when listing reminders or building the dashboard, compute Due status from stored due conditions and current data (today's date, latest visit mileage)
- expose due and pending reminders clearly to staff

Do not:
- add a background worker, hosted service, or separate worker project for reminder processing
- auto-create reminders when a visit is completed
- send real SMS/email/WhatsApp in MVP unless explicitly requested
- introduce RabbitMQ, Hangfire, or a job scheduling framework

---

## Security rules

Treat the project as if it handles real personal data.

Requirements:
- authentication is required
- authorization by role is required
- passwords must be hashed securely
- never log sensitive values carelessly
- do not expose database directly to the internet
- validate and sanitize input where relevant
- use HTTPS in deployed environments
- keep secrets outside source control

For MVP:
- internal business system
- security should be practical and real
- avoid fake enterprise complexity

---

## Logging and error handling

Requirements:
- log important application actions
- log failures with enough detail for debugging
- avoid noisy useless logs
- avoid leaking sensitive personal data in logs

Use centralized exception handling in API.
Return consistent error responses.

---

## Testing rules

Add tests for:
- reminder due evaluation logic (date and mileage)
- visit completion logic
- total cost calculation
- validation rules
- critical query/use-case logic

Do not chase meaningless coverage.
Prioritize high-value tests.

Prefer:
- unit tests for business rules
- integration tests for important persistence flows when useful

---

## Docker and local development

The project must run locally through Docker Compose.

Expected services:
- api
- postgres
- optionally web
- optionally pgadmin only if explicitly requested

Requirements:
- stable local startup
- documented environment variables
- database persistence via volumes
- easy first-run setup

The project should be easy to run for reviewers:
- clone
- configure env
- `docker compose up`
- apply migrations
- use

---

## UI rules

UI exists to support the business workflow, not to impress with frontend complexity.

Prefer:
- simple internal admin UI
- practical forms
- lists with filters
- readable visit history
- clear reminder screens

Do not:
- build flashy UI
- spend time on animations
- build a design system
- introduce Angular unless explicitly requested

Keep UI minimal and functional.

---

## Business realism rules

Always prefer realistic business flow over abstract purity.

Examples:
- auto services often need quick client lookup by phone or plate
- vehicle history must be fast to inspect
- reminders are created manually per vehicle because maintenance intervals vary; staff should control what gets tracked
- mileage belongs on visits, not on the vehicle record
- reminder due logic must be understandable by staff
- service catalog should support both standard services and manual entries

Whenever making a design choice, ask:
1. Does this help the staff do real work?
2. Does this keep the project understandable in interviews?
3. Is this still small enough for an MVP?

If the answer is no, do not add it.

---

## When implementing new tasks

For each non-trivial task:
1. briefly state the plan
2. list files to create or modify
3. implement only the requested scope
4. keep changes coherent and minimal
5. avoid unrelated refactoring
6. explain tradeoffs briefly when relevant

Do not silently redesign the whole project during a small task.

---

## Code generation constraints for agents

When generating code:
- favor complete, compilable code
- do not invent missing dependencies without stating them
- do not leave TODO placeholders unless explicitly allowed
- do not output pseudo-code when actual code is expected
- do not create dead abstractions
- do not split code into too many files without a reason

When editing existing files:
- preserve current style unless it is clearly harmful
- do not rename everything just because
- do not introduce breaking changes without necessity
- do not modify unrelated files

---

## Definition of done

A feature is done only when:
- core business flow works
- code compiles
- migrations are added when schema changed
- API contracts are clear
- validation exists
- basic tests exist for important logic
- docker/local startup still works
- no obvious overengineering was introduced

---

## Priority order

When forced to choose, prefer this order:
1. correctness
2. simplicity
3. business usefulness
4. maintainability
5. interview value
6. extensibility
7. cleverness

Never prioritize cleverness over clarity.