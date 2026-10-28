# AGENT_TASK_STYLE.md

Before writing code:
- restate the task briefly
- mention assumptions
- keep scope narrow

While writing code:
- prefer explicit, boring solutions
- keep architecture consistent
- do not add libraries without need
- do not overabstract
- do not introduce RabbitMQ
- do not introduce microservices
- do not introduce generic repository pattern

For this project:
- backend first
- realistic small auto service workflow
- one-service MVP
- modular monolith
- PostgreSQL + EF Core migrations + Docker Compose + worker
- minimal Blazor UI only

Always optimize for:
- real usability
- clean backend
- interview-friendly codebase