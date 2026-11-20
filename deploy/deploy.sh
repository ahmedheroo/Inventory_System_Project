#!/usr/bin/env bash
# ============================================================
# deploy.sh — Simple Deployment Script (Grad Project)
# ============================================================
# What this script does, step by step:
#   1. Reads the .env file for database passwords and secrets
#   2. Pulls the latest Docker images from Docker Hub
#   3. Restarts all containers with the new images
# ============================================================

set -e   # Stop the script immediately if any command fails

# ------ Configuration (with safe defaults) ------------------
COMPOSE_FILE="${COMPOSE_FILE:-docker-compose.prod.yml}"
IMAGE_TAG="${IMAGE_TAG:-latest}"
ENV_FILE="${ENV_FILE:-.env}"
# ------------------------------------------------------------

echo "========================================"
echo "  Starting Deployment"
echo "  Image Tag : ${IMAGE_TAG}"
echo "  Env File  : ${ENV_FILE}"
echo "========================================"

# Step 1: Check the compose file exists
if [ ! -f "$COMPOSE_FILE" ]; then
    echo "ERROR: Cannot find '${COMPOSE_FILE}'. Are you in the right folder?"
    exit 1
fi

# Step 2: Load environment variables from .env if it exists
if [ -f "$ENV_FILE" ]; then
    echo "Loading environment variables from ${ENV_FILE}..."
    set -o allexport
    source "$ENV_FILE"
    set +o allexport
fi

export IMAGE_TAG

# Step 3: Pull the latest images from Docker Hub
echo ""
echo "Pulling latest images..."
docker compose -f "$COMPOSE_FILE" pull

# Step 4: Start/restart all containers
echo ""
echo "Starting containers..."
docker compose -f "$COMPOSE_FILE" up -d

echo ""
echo "========================================"
echo "  Deployment Complete!"
echo "  Services are starting up."
echo "  Wait 30 seconds then check:"
echo "  http://localhost:5159  (Web Console)"
echo "========================================"
