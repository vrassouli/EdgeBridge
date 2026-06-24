# EdgeBridge TODO

## Recently Completed

- Added a lightweight test harness for protocol serialization, command correlation, mock device behavior, watch unsubscribe behavior, and client health/reconnect behavior.
- Fixed watch lifecycle with client unsubscribe messages and Agent-side subscription cancellation.
- Added client SDK connection health reporting and explicit reconnect support.
- Added configuration-driven Linux GPIO/PWM motor mapping.

## Next Phases

- Expand client-agent integration coverage against a real WebSocket listener.
- Verify and document the Linux systemd installation flow.
- Add authentication and authorization to the WebSocket transport.
- Add provisioning implementations for WiFi and Bluetooth setup.
- Add camera stream protocol and transport support.
- Add ADRs for protocol versioning and transport replacement.
