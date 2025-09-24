$ErrorActionPreference = 'Stop'

# Remove all Docker images that have not been used for 7 days.
docker image prune -a --filter "until=168h" --force

# Pull this repo.
git pull