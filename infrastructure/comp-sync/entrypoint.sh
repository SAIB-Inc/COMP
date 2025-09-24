#!/bin/bash
set -e

# Start socat in background to proxy Unix socket if CARDANO_NODE_SOCKET_TCP is set
if [ ! -z "$CARDANO_NODE_SOCKET_TCP" ]; then
    echo "Starting socat proxy for Cardano node socket..."
    echo "Proxying $CARDANO_NODE_SOCKET_TCP to /ipc/node.socket"
    socat UNIX-LISTEN:/ipc/node.socket,fork,reuseaddr,unlink-early TCP:$CARDANO_NODE_SOCKET_TCP &
    SOCAT_PID=$!
    echo "Socat started with PID: $SOCAT_PID"

    # Wait for socket to be available
    sleep 2
fi

# Start the .NET application
echo "Starting COMP.Sync..."
exec dotnet COMP.Sync.dll