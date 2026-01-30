I will add a PostgreSQL database service to the Docker infrastructure and configure the application to use it when running in a container.

### Plan
1.  **Update docker-compose.yml**:
    *   Add a `postgres` service with default credentials (user: `postgres`, password: `password`, db: `flowos`).
    *   Expose port `5432` locally.
    *   Add a `db-data` volume for persistence.
    *   Update `flowos-api` to depend on `postgres` and set the `ConnectionStrings__DefaultConnection` environment variable.
2.  **Update Program.cs**:
    *   Modify the `AddDbContext` logic.
    *   Check `builder.Configuration.GetConnectionString("DefaultConnection")`.
    *   If present, use `options.UseNpgsql(...)`.
    *   If missing, fallback to `options.UseInMemoryDatabase(...)`.

### Modified Files
*   `docker-compose.yml`
*   `src/FlowOS.API/Program.cs`