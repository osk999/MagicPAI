# scripts/temporal-cli.ps1
# Wrapper around Temporal CLI via docker exec. See temporal.md §UU.4.
# Usage: ./scripts/temporal-cli.ps1 workflow list
#        ./scripts/temporal-cli.ps1 workflow cancel --workflow-id mpai-abc
#
# Note: the temporal auto-setup image binds gRPC to the container's own IP,
# not 127.0.0.1, so we resolve it via `hostname -i` inside the container.
$addr = (docker exec mpai-temporal hostname -i).Trim()
docker exec mpai-temporal temporal --address "${addr}:7233" --namespace magicpai @args
