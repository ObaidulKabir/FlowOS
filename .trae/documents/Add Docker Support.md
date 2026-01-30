I will add Docker support to the FlowOS project.

### Plan
1.  **Create Dockerfile**: Create a `Dockerfile` in `src/FlowOS.API/` to build the API image.
    *   It will be a multi-stage build (Build, Publish, Final).
    *   It needs to copy all project files (`.csproj`) to restore dependencies.
    *   It will copy the `flowos-config` folder so the container has access to default configurations.
2.  **Create docker-compose.yml**: Create a `docker-compose.yml` in the solution root.
    *   It will define the `flowos-api` service.
    *   It will map port `5005` (or similar) to the host.
    *   It will mount the `flowos-config` volume for easier updates.
3.  **Create .dockerignore**: Add a `.dockerignore` file to exclude unnecessary files from the build context.

### New Files
*   `src/FlowOS.API/Dockerfile`
*   `docker-compose.yml`
*   `.dockerignore`