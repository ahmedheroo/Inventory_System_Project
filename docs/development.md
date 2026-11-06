# Developer setup

## 1. Prerequisites

- .NET SDK 8.0+ (`dotnet --list-sdks`)
- Docker Desktop — needed for SQL Server and RabbitMQ, optional for the Product service because of
  the SQLite fallback
- EF Core CLI: `dotnet tool install --global dotnet-ef`

## 2. Build and test

```bash
dotnet restore
dotnet build InventorySystem.sln
dotnet test  InventorySystem.sln
```

Expected: the solution builds with 0 warnings, and 442 tests pass across eleven projects — 21 alert
domain, 22 alert HTTP, 22 product domain, 19 product HTTP, 33 inventory domain, 58 inventory HTTP
(the concurrency and outbox cases, and the live broadcast), 35 reporting HTTP (the projection, the
four reports and the CSV/Excel downloads), 57 identity domain and 41 identity HTTP (accounts, roles
and the tokens the rest of the system validates), 120 console tests (signing in, what each role may
open, signing out, and the controllers against faked service clients, the live regions and the
station), and 14 cross-service contract tests. No broker, no database and no running service is
needed: the messaging and HTTP seams are driven by in-memory fakes.

## 3. Run the services

```bash
# terminal 1 — products, categories, warehouses
dotnet run --project src/Services/Product/Product.Api   # http://localhost:5251/swagger

# terminal 2 — stock levels and the stock ledger (validates against the Product service)
dotnet run --project src/Services/Inventory/Inventory.Api   # http://localhost:5102/swagger

# terminal 3 — low-stock alerts (consumes stock events from RabbitMQ)
dotnet run --project src/Services/Alert/Alert.Api   # http://localhost:5212/swagger

# terminal 4 — inventory, low-stock, movement and valuation reports (consumes the same events)
dotnet run --project src/Services/Reporting/Reporting.Api   # http://localhost:5312/swagger

# terminal 5 — accounts, roles and the tokens the rest of the system validates
dotnet run --project src/Services/Identity/Identity.Api   # http://localhost:5402/swagger

# terminal 6 — the console a person works in (reads the five services above over HTTP)
dotnet run --project src/Inventory.Web   # http://localhost:5159
```

The console owns no data and no database: it calls the five service APIs and reports a service that
is down as such instead of showing an empty table. All six apps can also be built and run as containers
instead of in six terminals — see section 4. Its base URLs come from configuration
(`Services:ProductApi:BaseUrl`, `Services:InventoryApi:BaseUrl`, `Services:AlertApi:BaseUrl`,
`Services:ReportingApi:BaseUrl`, `Services:IdentityApi:BaseUrl` in `src/Inventory.Web/appsettings.json`),
so pointing it at another host needs no rebuild.

Every screen sits behind a session, so the console signs you in the first time you open it. In
`Development` the Identity service seeds one account per role — `manager`, `clerk` and `viewer`,
all with the password `inventory-console-2026` — and the console keeps its own cookie rather than the
token: it presents its own `console` credential when it needs to follow the Inventory service's live
stock view (relay-side only; a browser never sees a token). The pages it serves, and the design it
follows, are in [web-ui.md](web-ui.md).

### Live updates

The overview and the stock levels screen follow the Inventory service's hub and re-read their own
regions when stock moves, and the rail's alert count keeps itself current on every screen. Three
pieces make that work, and only the first is a service:

| Piece | Where | What it does |
| ----- | ----- | ------------ |
| `StockHub` at `/hubs/stock` | Inventory service | broadcasts one notification per committed stock change, with the origin it was given |
| `wwwroot/js/live.js` | console | follows the hub, re-reads the stale regions from the console itself |
| `/live/attention`, `/live/overview`, `/live/levels` | console | the same server-rendered fragments the pages are built from |
| `X-Origin-Id` on stock operations | console → Inventory | names the station (browser) that asked, so a screen can tell its own movements from everybody else's |

The station is a cookie the console mints per browser (`StationIdMiddleware`), sent on writes by
`StationHeaderHandler` and sanitised and echoed back by the Inventory service. It is a label, not a
credential: nothing is authorised by it, and it is visible to every client on the hub — until sign-in
exists, "who" is a browser, not a person.

Nothing is required for a page to render: without JavaScript, or with the hub unreachable, every
screen works as it is served and simply does not refresh itself. Two settings matter when the browser
is not on the machine that runs the services: `Services:InventoryApi:HubUrl` (where the *browser*
reaches the hub; it defaults to the Inventory service's base URL plus `/hubs/stock`) and the
Inventory service's `Cors:AllowedOrigins` (empty outside `Development`, so a console on another
origin has to be listed there — the hub is a browser connection and CORS applies to it).

Start the Product service first: every stock operation verifies its product and warehouse through it,
and an unreachable catalogue is reported as HTTP 503. RabbitMQ is optional for the services to
*start* (the connection is opened lazily and the Alert consumer keeps retrying), but without it
nothing is delivered: the Inventory service accumulates events in its outbox, the Alert and Reporting
services have nothing to project, and the live screens will show the movement on the stock page (the
Inventory service answers that one directly) while the overview and the rail stay where they are.

In `Development` each service:

1. creates the schema (SQLite: `EnsureCreated`, SQL Server: `Migrate`);
2. the Product service seeds three categories, two warehouses and four products;
3. the Identity service seeds the three console accounts (`manager`, `clerk`, `viewer` — password
   `inventory-console-2026`) and the console's own relay account (`console`);
4. all five enable Swagger UI, and the four data services allow any origin for the MVC app (the
   console calls Identity server to server, so it needs no CORS there).

### Choosing a database

| Service | `Database:Provider` | Connection string | Where it is applied |
| ------- | ------------------- | ----------------- | ------------------- |
| Product | `SqlServer` | `Server=localhost,1433;Database=ProductDb;…` | `appsettings.json` (default) |
| Product | `Sqlite` | `Data Source=product.dev.db` | `appsettings.Development.json` (fallback) |
| Inventory | `SqlServer` | `Server=localhost,1433;Database=InventoryDb;…` | `appsettings.json` (default) |
| Inventory | `Sqlite` | `Data Source=inventory.dev.db` | `appsettings.Development.json` (fallback) |
| Alert | `SqlServer` | `Server=localhost,1433;Database=AlertDb;…` | `appsettings.json` (default) |
| Alert | `Sqlite` | `Data Source=alert.dev.db` | `appsettings.Development.json` (fallback) |
| Reporting | `SqlServer` | `Server=localhost,1433;Database=ReportingDb;…` | `appsettings.json` (default) |
| Reporting | `Sqlite` | `Data Source=reporting.dev.db` | `appsettings.Development.json` (fallback) |
| Identity | `SqlServer` | `Server=localhost,1433;Database=IdentityDb;…` | `appsettings.json` (default) |
| Identity | `Sqlite` | `Data Source=identity.dev.db` | `appsettings.Development.json` (fallback) |

The Inventory **and** Reporting services also need `Services:ProductApi:BaseUrl` (default
`http://localhost:5251`): Inventory validates stock operations against the catalogue, Reporting
resolves sku, names and unit prices when a report is rendered. The console needs all five base URLs,
including `Services:IdentityApi:BaseUrl` (`http://localhost:5402`) and its own `ClientId` /
`ClientSecret`, which are what it signs in *as itself* with.

Override without editing files:

```bash
Database__Provider=SqlServer \
ConnectionStrings__ProductDb="Server=localhost,1433;Database=ProductDb;User Id=sa;Password=InventoryDB@123;TrustServerCertificate=True" \
dotnet run --project src/Services/Product/Product.Api
```

## 4. Containers

### 4.1 The whole system

```bash
cp .env.example .env           # MSSQL_SA_PASSWORD, RABBITMQ_*, ASPNETCORE_ENVIRONMENT, JWT_SIGNING_KEY
docker compose up -d --build
docker compose ps              # two infrastructure containers + six apps, all healthy
```

The six apps are built from one [Dockerfile](../Dockerfile): which project to publish is the only thing
that differs between them, so that is a build argument rather than six copies of the same twenty
instructions. The build context is the repository root, because every app references projects outside
its own folder; and the source copy and the solution-wide `dotnet restore` sit deliberately *above* the
`PROJECT` argument, so both layers are identical for all six images and are built once per
`docker compose build`.

| App | Container | Host port | Waited for before it starts |
| --- | --------- | --------- | --------------------------- |
| Product | `inventory-product-api` | 5251 | SQL Server healthy |
| Inventory | `inventory-inventory-api` | 5102 | SQL Server, RabbitMQ, Product started |
| Alert | `inventory-alert-api` | 5212 | SQL Server, RabbitMQ |
| Reporting | `inventory-reporting-api` | 5312 | SQL Server, RabbitMQ, Product started |
| Identity | `inventory-identity-api` | 5402 | SQL Server healthy |
| Console | `inventory-console` | 5159 | Identity **healthy**; the other four started |

Every app listens on 8080 inside its container and publishes the same host port a local `dotnet run`
uses, so the URLs in the README hold either way. Inside the network nothing is `localhost`: the console,
the Inventory service and the Reporting service are pointed at `http://product-api:8080` and the like,
and the broker is `rabbitmq:5672`.

The five services answer `/health` — 200 only when the service is up *and* its database answers — and
the console has no `/health` because it is a page rather than a probe, so its health check is
`/sign-in`. That is what `depends_on` reads: the console waits for Identity to be *healthy*, because
nothing can be done without signing in, and only for the other four to be *started*, because a screen
whose service is down says so rather than showing an empty table.

### 4.2 What the apps are given

`ASPNETCORE_ENVIRONMENT` defaults to `Development` here, exactly as a local run does. That is what seeds
the sample catalogue, the three accounts to sign in with and the console's own relay account, and what
turns Swagger on. Each app is additionally given `Database__Provider=SqlServer` and its connection
string, which override the SQLite fallback that Development would otherwise select — the two settings
that *must* differ in a container, because `localhost` is now the container itself.

| Key in `.env` | Used by | Meaning |
| ------------- | ------- | ------- |
| `MSSQL_SA_PASSWORD` | SQL Server, and every app's connection string | Applied when the volume is created: changing it later does not change the password stored in an existing `sqlserver-data`, and the services then fail to sign in with `Login failed for user 'sa'` |
| `RABBITMQ_USER` / `RABBITMQ_PASSWORD` | RabbitMQ, Inventory, Alert, Reporting | Same caveat as above — the broker's users come from its volume |
| `ASPNETCORE_ENVIRONMENT` | every app | `Development` also means the Inventory service allows any browser origin; the console's live view is server-side relayed, so nothing in the browser needs it |
| `JWT_SIGNING_KEY` | Identity (signs), Inventory (validates) | The default is the key published in `appsettings.json`: development only |

### 4.3 A production run

```bash
ASPNETCORE_ENVIRONMENT=Production JWT_SIGNING_KEY=$(openssl rand -base64 48) \
  docker compose up -d --build
```

Two things then have to be supplied, because nothing is seeded outside `Development` and the published
key is refused there:

- **A real signing key** of 32+ characters. Identity and Inventory *refuse to start* while
  `Jwt:SigningKey` is still the published development key (`EnsureUsableFor`), which is the intended
  failure: anybody who has read this repository could otherwise mint a token they would accept.
- **Accounts.** Nothing exists to sign in with until `Identity__Seed__Enabled=true` together with
  `Identity__Seed__UserPassword` and `Identity__Seed__ServiceSecret` is set — and the service secret
  must equal the console's `Services__IdentityApi__ClientSecret`, which is the account the console signs
  in *as itself* with to follow the live stock view. A console without it still serves every screen; it
  simply stops relaying movements.

The stack is HTTP. A real deployment terminates TLS in front of it (the console's session cookie has
`CookieSecurePolicy.SameAsRequest`, so HTTPS in front means a secure cookie), and publishes only the
console if the services do not need to be reachable from outside.

### 4.4 Infrastructure only

For the host-run workflow (option B in the README) — SQL Server and RabbitMQ, without the apps:

```bash
cp .env.example .env          # change MSSQL_SA_PASSWORD / RABBITMQ_* if you like
docker compose up -d sqlserver rabbitmq
docker compose ps             # both services should report "healthy"
```

- SQL Server: `localhost,1433`
- RabbitMQ AMQP: `localhost:5672`, management UI: <http://localhost:15672>

Stop with `docker compose down` (add `-v` to also drop the data volumes).

## 5. Messaging: RabbitMQ and the outbox

Both services read the same `RabbitMq` section; the defaults match `docker-compose.yml`, so a local
run needs no configuration.

| Key | Default | Meaning |
| --- | ------- | ------- |
| `RabbitMq:Enabled` | `true` | `false` runs the service without a broker: Inventory keeps events in the outbox, Alert and Reporting stop consuming (the reports then keep serving the read model they already have) |
| `RabbitMq:HostName` / `Port` | `localhost` / `5672` | AMQP endpoint |
| `RabbitMq:UserName` / `Password` | `inventory` / `inventory` | credentials (change them in `.env` for the container) |
| `RabbitMq:VirtualHost` | `/` | vhost |
| `RabbitMq:ClientName` | per service | the name the broker shows in its connections list |
| `RabbitMq:ConnectionTimeoutSeconds` | `10` | per-attempt TCP timeout |

The Inventory service additionally owns the dispatcher:

| Key | Default | Meaning |
| --- | ------- | ------- |
| `Outbox:PollSeconds` | `5` | how often pending events are looked for |
| `Outbox:BatchSize` | `50` | events per pass |
| `Outbox:InitialRetrySeconds` | `2` | backoff after the first failure |
| `Outbox:MaxRetrySeconds` | `60` | backoff cap (there is no attempt limit) |

Override with environment variables, e.g. to point a service at another broker:

```bash
RabbitMq__HostName=rabbitmq RabbitMq__Port=5672 dotnet run --project src/Services/Alert/Alert.Api
```

What the endpoints look like:

- exchange `inventory.events` (topic, durable), messages published persistent with confirms;
- queue `alert.inventory` bound with `stock.*`, with `x-dead-letter-exchange` = `inventory.events.dlx`;
- dead-letter queue `alert.inventory.dead` (bound with `#`), where a message lands after one failed
  retry, or immediately when its routing key is unknown — a poison message never blocks the queue;
- `reporting.inventory` (with `reporting.inventory.dead`) is bound the same way and consumed by the
  Reporting service, which projects each event into its read model.

Manually verifying delivery (as done for the Sprint 4 live check):

```bash
docker exec inventory-rabbitmq rabbitmqctl list_queues name messages consumers
docker exec inventory-rabbitmq rabbitmqctl list_exchanges name type
# management UI: http://localhost:15672 (inventory / inventory)
```

Stop the broker (`docker stop inventory-rabbitmq`), change stock, then start it again: the events
are still in `OutboxMessages`, the dispatcher logs each failed attempt with the next attempt time,
and the alerts appear as soon as the broker is back. Nothing is lost.

## 6. Working with migrations

Migrations target SQL Server only and are generated through each service's design-time factory
(`ProductDbContextFactory`, `InventoryDbContextFactory`, `AlertDbContextFactory`), which pins the
provider regardless of the current environment:

```bash
cd src/Services/Product/Product.Infrastructure
dotnet ef migrations add <Name> --output-dir Persistence/Migrations
dotnet ef migrations script --idempotent -o ../../../artifacts/productdb.sql

cd ../../../src/Services/Inventory/Inventory.Infrastructure
dotnet ef migrations add <Name> --output-dir Persistence/Migrations
dotnet ef migrations script --idempotent -o ../../../artifacts/inventorydb.sql

cd ../../../src/Services/Alert/Alert.Infrastructure
dotnet ef migrations add <Name> --output-dir Persistence/Migrations
dotnet ef migrations script --idempotent -o ../../../artifacts/alertdb.sql

cd ../../../src/Services/Reporting/Reporting.Infrastructure
dotnet ef migrations add <Name> --output-dir Persistence/Migrations
dotnet ef migrations script --idempotent -o ../../../artifacts/reportingdb.sql
```

The Reporting service has no `Domain` project: its read-model types live in the Application layer,
and `ReportingDbContextFactory` is the design-time factory that keeps its migrations on SQL Server.

> Do not rely on the CLI's environment default here: with `ASPNETCORE_ENVIRONMENT=Development` the
> app itself would select SQLite. The design-time factory exists precisely to prevent that.

Applying migrations to a live SQL Server happens automatically at API startup
(`ProductDbInitializer.InitializeAsync`, and the equivalent initializer in each service), and so does
the seed data a service owns.

### Applying every database to SQL Server, seeded

Five databases are created this way — `ProductDb`, `IdentityDb`, `InventoryDb`, `AlertDb` and
`ReportingDb` — with `ProductDb` and `IdentityDb` carrying seed data. The environment variables
override the SQLite fallback, and each service is started from its own directory (which is what keeps
`appsettings.Development.json`, and therefore the seeds, in play; `dotnet run --project` does the
same thing):

```bash
cp .env.example .env          # and use the same password below
MSSQL_SA_PASSWORD=InventoryDB@123

apply() {
  ( cd "src/Services/$1/$1.Api" &&
    Database__Provider=SqlServer \
    ConnectionStrings__"$2"="Server=localhost,1433;Database=$2;User Id=sa;Password=${MSSQL_SA_PASSWORD};TrustServerCertificate=True" \
    dotnet run )
}

apply Product   ProductDb
apply Identity  IdentityDb
apply Inventory InventoryDb
apply Alert     AlertDb
apply Reporting ReportingDb
```

Each run is an API, not a migration tool: stop it once it logs `<X>Db is ready.` (the Identity seed
adds a line naming the accounts it created). To see what landed:

```bash
MSYS_NO_PATHCONV=1 docker exec inventory-sqlserver /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P "${MSSQL_SA_PASSWORD}" -C -h -1 -W \
  -Q "SET NOCOUNT ON; SELECT name FROM sys.databases ORDER BY name;"
```

`InventoryDb`, `AlertDb` and `ReportingDb` start empty on purpose: stock, alerts and the read model
are earned at runtime — a stock movement is written by the Inventory API and projected into the other
two by their consumers.

## 7. Integration tests

`tests/*.Api.Tests` boot the real APIs with `WebApplicationFactory<Program>` on a throwaway SQLite
database per test, so the suite exercises the genuine HTTP pipeline, model validation, middleware and
EF Core, and never touches your development data. The messaging seams are replaced with in-memory
fakes (a recording publisher, a feedable consumer), which is why the suite needs no broker.

`tests/Reporting.Api.Tests` follows the same recipe and additionally replaces the Product service
with a fake catalogue, so the reports are asserted against known sku, names and unit prices. The
consumer under test is the **real** hosted service, driven through the `IEventConsumer` feed — the
broker hop is the only thing that is not real.

## 8. Troubleshooting

| Symptom | Cause / fix |
| ------- | ----------- |
| `Could not copy ... .dll ... file is locked` | a dev server is still running — stop it before rebuilding |
| `Connection string 'ProductDb' is not configured` | set `ConnectionStrings:ProductDb` (or `ConnectionStrings__ProductDb`) |
| `error during connect: ... dockerDesktopLinuxEngine` | Docker Desktop is not running; either start it or stay on the SQLite fallback |
| Containerised services log `Login failed for user 'sa'` | the `sqlserver-data` volume was created with a different password (the SA password is only read when the volume is new). Put that password in `MSSQL_SA_PASSWORD`, or rebuild the volume with `docker compose down -v` |
| `docker compose ps` shows an app as `unhealthy` while the others are fine | that app cannot reach its database: read its own log (`docker compose logs <service>`) — the startup line naming the database it could not open is the one that matters |
| `Bind for 0.0.0.0:5159 failed: port is already allocated` | a `dotnet run` from option B is still listening on that port; stop it, or publish the containers on other ports |
| Everybody is signed out of the console after a deploy | the console's data-protection keys live in its own container, next to its session cookie (the log says so at startup). `docker compose restart console` keeps them; recreating the container — which is what a rebuild does — does not. A deployment that should survive that mounts a key ring into the container |
| Migration generates `Sqlite:Autoincrement` | migrations were generated without the design-time factory; delete and regenerate |
| Stock operations return 503 | the Inventory service could not reach the Product service; start it (or check `Services:ProductApi:BaseUrl`) |
| Stock operations return 409 | a concurrent operation changed the same stock row; retry from a refreshed stock level |
| Stock changes succeed but no alerts appear | the broker is down or `RabbitMq:Enabled` is false — the events are safe in `OutboxMessages`; check `docker compose ps` and the Inventory log for `Publishing … failed on attempt …` |
| The outbox never drains | the Inventory log shows a connect error per attempt; confirm RabbitMQ on `localhost:5672` and the credentials in the `RabbitMq` section |
| An alert keeps coming back | you are re-publishing an event with an id that was already processed; the consumer ignores duplicates, so a new shortage needs a new event |
| `alert.inventory.dead` keeps growing | messages could not be handled: inspect them in the management UI. A malformed payload or an unregistered routing key is dead-lettered on purpose |
| Reports return 503 | the Reporting service could not reach the Product service (`Services:ProductApi:BaseUrl`) — reports are enriched with catalogue names and prices, so an unavailable catalogue is reported rather than shown as "no stock" |
| Reports are empty or stale | nothing has been projected yet: check that the broker is up, that `reporting.inventory` has a consumer (`docker exec inventory-rabbitmq rabbitmqctl list_queues name messages consumers`) and that the Inventory service has drained its outbox |
| A movement value looks wrong in a report | remember a transfer is two rows (out of the source, into the destination) and an adjustment stores the signed delta, so per-warehouse sums differ from the product-level one |
