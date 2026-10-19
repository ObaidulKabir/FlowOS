#!/usr/bin/env bash
# ==============================================================================
# FlowOS Multi-VPS Cluster & Load Balancing Test Script (Bash)
# ==============================================================================

set -e

BASE_URL="${1:-http://localhost:5183}"
REQUESTS_COUNT="${2:-9}"

echo -e "\033[36m==========================================================\033[0m"
echo -e "\033[36m       FlowOS Multi-VPS Load Balancing Cluster Test       \033[0m"
echo -e "\033[36m==========================================================\033[0m"
echo -e "\033[33mTarget Load Balancer URL: ${BASE_URL}\033[0m"
echo ""

# 1. Query Cluster Topology Discovery Endpoint
echo -e "\033[32m[1/3] Querying Load Balancer Cluster Topology (/health/cluster)...\033[0m"
curl -s -f http://localhost/health/cluster || echo "Cluster discovery endpoint not reachable yet."
echo -e "\n"

# 2. Test Load Balancing Distribution
echo -e "\033[32m[2/3] Sending ${REQUESTS_COUNT} consecutive requests to verify traffic distribution...\033[0m"
for i in $(seq 1 "${REQUESTS_COUNT}"); do
    SERVED_BY=$(curl -s -I "${BASE_URL}/health" | grep -i "x-served-by" | tr -d '\r' | awk '{print $2}')
    echo -e "  Request #${i} -> Served by VPS Node: \033[35m${SERVED_BY:-Unknown}\033[0m"
    sleep 0.2
done

echo ""
# 3. Test Direct VPS Node Ports
echo -e "\033[32m[3/3] Checking direct access to individual VPS ports...\033[0m"
for PORT in 5181 5182 5184; do
    STATUS=$(curl -s -o /dev/null -w "%{http_code}" "http://localhost:${PORT}/health" || echo "FAIL")
    echo "  Port ${PORT} direct health status: ${STATUS}"
done

echo ""
echo -e "\033[36m==========================================================\033[0m"
echo -e "\033[36mCluster verification completed.\033[0m"
echo -e "\033[36m==========================================================\033[0m"
