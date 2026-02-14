# Database Persistence and Ports
Date: 2026-02-14

## What Is Configured
AppHost now pins stable host ports and enables a persistent Postgres bind-mounted data folder:

- Postgres host port: `5432`
- pgAdmin host port: `5050`
- Postgres data folder: `data/db` (repo-local bind mount)

Implementation file:

- `CognitiveMemory.AppHost/Program.cs`

## pgAdmin Connection Settings
When creating/viewing the Postgres server in pgAdmin, use:

- Host: `localhost`
- Port: `5432`
- Database: `memorydb`
- Username/password: the Postgres credentials provisioned by Aspire

Because the Postgres host port is pinned, pgAdmin and other tools can reuse the same port consistently across restarts.
