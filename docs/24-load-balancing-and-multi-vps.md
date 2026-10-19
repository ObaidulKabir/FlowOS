# Chapter 24 — Load Balancing & Multi-VPS Cluster Setup

This chapter explains how to run FlowOS in a high-availability, load-balanced multi-node cluster that accurately simulates independent Virtual Private Servers (VPSs) connected across a single private network (VPC/LAN).

---

## 1. Architectural Overview

In a production environment, deploying FlowOS across multiple VPS instances provides horizontal scalability, zero-downtime rolling upgrades, and fault tolerance against node failures.

```
                         [ Public Traffic / Clients / Tests ]
                                          │
                                          ▼
                      ┌───────────────────────────────────────┐
                      │          VPS 0: Load Balancer         │
                      │         (NGINX Ingress Gateway)       │
                      │               10.20.0.10              │
                      │    Ports: 80, 5183, 8080, 8081        │
                      └───────────────────┬───────────────────┘
                                          │
                   ┌──────────────────────┼──────────────────────┐
                   │                      │                      │
                   ▼                      ▼                      ▼
        ┌────────────────────┐ ┌────────────────────┐ ┌────────────────────┐
        │       VPS 1        │ │       VPS 2        │ │       VPS 3        │
        │   (FlowOS API 1)   │ │   (FlowOS API 2)   │ │   (FlowOS API 3)   │
        │    10.20.0.11      │ │    10.20.0.12      │ │    10.20.0.13      │
        │  Debug Port: 5181  │ │  Debug Port: 5182  │ │  Debug Port: 5184  │
        └──────────┬─────────┘ └──────────┬─────────┘ └──────────┬─────────┘
                   │                      │                      │
                   └──────────────────────┼──────────────────────┘
                                          │
                                          ▼
                      ┌───────────────────────────────────────┐
                      │          VPS DB: Database Node        │
                      │         (PostgreSQL 15 Cluster)       │
                      │               10.20.0.20              │
                      │            Port: 5432:5432            │
                      └───────────────────────────────────────┘
```

### Why a Shared Database VPS is Required for Load Balancing

When HTTP requests are load balanced across multiple VPS instances (e.g., round-robin or least-connections), any request might hit any node:
1. If VPS 1 and VPS 2 had disconnected, un-replicated databases, a workflow created on VPS 1 (`POST /api/workflows`) would return `404 Not Found` when a subsequent query or event landed on VPS 2.
2. In FlowOS, the database VPS (`10.20.0.20:5432`) maintains state persistence, workflow transitions, outbox events, and distributed leases.
3. FlowOS includes `DistributedLeaseService`, which leverages PostgreSQL (`INSERT ... ON CONFLICT DO UPDATE WHERE ExpiresAtUtc <= now`) for leader election and singleton background tasks (`OutboxProcessorService`, `WorkflowTimerProcessorService`, `AgentTaskProcessorService`). Multiple FlowOS nodes can run concurrently without race conditions.

---

## 2. Virtual VPS Private Network Topology

The cluster runs on a custom Docker bridge network (`flowos-vps-network`) simulating a private datacenter subnet:
* **Subnet:** `10.20.0.0/24`
* **Gateway:** `10.20.0.1`

| Node Name | Simulated VPS Role | Internal IP | Internal Hostname | External Exposed Port | Function |
|---|---|---|---|---|---|
| `vps-lb` | Gateway Load Balancer | `10.20.0.10` | `vps-lb.internal` | `80`, `5183`, `8080`, `8081` | NGINX reverse proxy, round-robin load distribution, cluster health |
| `vps1-flowos-api` | FlowOS Application Node 1 | `10.20.0.11` | `vps1.internal` | `5181` | FlowOS API Instance 1 |
| `vps2-flowos-api` | FlowOS Application Node 2 | `10.20.0.12` | `vps2.internal` | `5182` | FlowOS API Instance 2 |
| `vps3-flowos-api` | FlowOS Application Node 3 | `10.20.0.13` | `vps3.internal` | `5184` | FlowOS API Instance 3 |
| `vps-db` | Database Server | `10.20.0.20` | `vps-db.internal` | `5432` | PostgreSQL 15 database & distributed locks |
| `vps-dashboard` | Web Dashboard Node | `10.20.0.30` | `vps-dashboard.internal` | `3000` | FlowOS Web UI (routes API calls via Load Balancer) |
| `vps-mcp` | MCP AI Control Plane | `10.20.0.14` | `vps-mcp.internal` | `8082` | Streamable HTTP MCP server |

---

## 3. Quick Start

### Starting the Cluster
Run the dedicated cluster compose file:
```bash
docker-compose -f docker-compose.cluster.yml up -d --build
```

### Checking Status of the VPS Nodes
```bash
docker-compose -f docker-compose.cluster.yml ps
```

### Viewing Logs from the Load Balancer
```bash
docker-compose -f docker-compose.cluster.yml logs -f vps-lb
```

---

## 4. Verifying Load Balancing

### 1. Querying Cluster Discovery
The Load Balancer exposes a topology discovery endpoint:
```bash
curl http://localhost/health/cluster
```
Output:
```json
{
  "cluster": "FlowOS Virtual Multi-VPS Private Network",
  "status": "online",
  "subnet": "10.20.0.0/24",
  "gateway": "10.20.0.1",
  "load_balancer": {
    "name": "vps-lb",
    "ip": "10.20.0.10",
    "algorithm": "round_robin",
    "ingress_ports": [80, 5183, 8080, 8081]
  },
  "application_nodes": [
    { "name": "vps1", "ip": "10.20.0.11", "service": "flowos-api", "direct_host_port": 5181 },
    { "name": "vps2", "ip": "10.20.0.12", "service": "flowos-api", "direct_host_port": 5182 },
    { "name": "vps3", "ip": "10.20.0.13", "service": "flowos-api", "direct_host_port": 5184 }
  ]
}
```

### 2. Inspecting the `X-Served-By` Response Header
Every response routed through the Load Balancer includes an `X-Served-By` header indicating which backend VPS handled the request:
```bash
curl -I http://localhost:5183/health
```
Response:
```http
HTTP/1.1 200 OK
Server: nginx/1.27.0
X-Served-By: 10.20.0.11:8080
X-Load-Balancer: FlowOS-Cluster-LB
```
Repeating this request will rotate through `10.20.0.11:8080`, `10.20.0.12:8080`, and `10.20.0.13:8080`.

### 3. Running the Automated Test Script
A verification script is included to test distribution across the VPS nodes:

**PowerShell:**
```powershell
.\scripts\test-cluster.ps1
```

**Bash:**
```bash
./scripts/test-cluster.sh
```

---

## 5. Testing Inter-VPS Communication

Because all containers reside on the `flowos-vps-network` (`10.20.0.0/24`), each VPS can communicate with any other VPS using either its internal DNS hostname or static IP address:

```bash
# Ping VPS 2 from VPS 1
docker exec -it vps1-flowos-api ping -c 2 vps2.internal

# Verify database connectivity from VPS 3
docker exec -it vps3-flowos-api ping -c 2 10.20.0.20
```

---

## 6. Fault Tolerance & Failover Demonstration

To test automatic failover when a VPS goes offline:

1. Stop VPS 2:
   ```bash
   docker-compose -f docker-compose.cluster.yml stop vps2-flowos-api
   ```
2. Send requests to the Load Balancer:
   ```bash
   curl -I http://localhost:5183/health
   ```
   NGINX automatically detects that `10.20.0.12` is unreachable (`max_fails=3 fail_timeout=10s`) and seamlessly routes all traffic between `vps1` (`10.20.0.11`) and `vps3` (`10.20.0.13`) with zero client downtime.
3. Bring VPS 2 back online:
   ```bash
   docker-compose -f docker-compose.cluster.yml start vps2-flowos-api
   ```
   Traffic is restored to all 3 VPS nodes automatically.

---

## 7. Stopping the Cluster

To stop the cluster and preserve data:
```bash
docker-compose -f docker-compose.cluster.yml down
```

To stop the cluster and purge database volumes:
```bash
docker-compose -f docker-compose.cluster.yml down -v
```
