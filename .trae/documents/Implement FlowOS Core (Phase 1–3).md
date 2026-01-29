# FlowOS Initialization & Phase 1 Implementation Plan

This plan outlines the steps to initialize the FlowOS solution structure according to the strict modular monolith guidelines and implement the foundational "Phase 1 - Core Determinism" components (Tenant & Event models).

## 1. Project Scaffolding (Strict Modular Monolith)
Create the solution and projects using .NET 8 CLI.

### Structure
- **Solution**: `FlowOS.sln`
- **Src Projects**:
  - `FlowOS.Domain` (Class Library) - *Core entities, no dependencies*
  - `FlowOS.Events` (Class Library) - *Event schemas & interfaces*
  - `FlowOS.StateMachines` (Class Library) - *State validation engine*
  - `FlowOS.Security` (Class Library) - *Roles & Policies*
  - `FlowOS.Workflows` (Class Library) - *Workflow runtime*
  - `FlowOS.Agents` (Class Library) - *AI integration*
  - `FlowOS.Application` (Class Library) - *Use cases & orchestration*
  - `FlowOS.Infrastructure` (Class Library) - *DB, MQ, External adapters*
  - `FlowOS.Api` (Web API) - *Entry point*
- **Test Project**:
  - `FlowOS.UnitTests` (xUnit)

### Dependency Graph Setup
- **Domain**: No dependencies.
- **Events**: Depends on `Domain`.
- **StateMachines**, **Security**, **Workflows**, **Agents**: Depend on `Domain` and `Events`.
- **Application**: Depends on all the above modules.
- **Infrastructure**: Depends on `Application` (for interfaces) and `Domain`.
- **Api**: Depends on `Application` and `Infrastructure`.

## 2. Phase 1 Implementation: Core Determinism

### 2.1 Tenant Model (`FlowOS.Domain`)
Implement the Tenant entity as the logical isolation boundary.
- `TenantId` (Guid/String)
- `Name`
- `Status` (Active/Suspended)
- `Configuration` (JSONB representation)

### 2.2 Event Model (`FlowOS.Events`)
Implement the immutable event base structure.
- `IEvent` interface.
- Base `DomainEvent` class.
- **Metadata**:
  - `EventId` (UUID)
  - `TenantId` (Mandatory)
  - `Timestamp` (UTC)
  - `EventVersion`
  - `CorrelationId`

### 2.3 Persistence Foundations (`FlowOS.Infrastructure`)
- Set up EF Core with PostgreSQL support.
- Define `FlowOSDbContext`.
- Configure `Tenant` and `EventStore` (append-only) mappings.

## 3. Verification
- Create unit tests in `FlowOS.UnitTests` to verify:
  - Tenant creation.
  - Event immutability and structure.
  - Project dependency correctness (compilation check).
