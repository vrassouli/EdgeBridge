# EdgeBridge TODO

## Recently Completed

- Added a lightweight test harness for protocol serialization, command correlation, mock device behavior, watch unsubscribe behavior, and client health/reconnect behavior.
- Fixed watch lifecycle with client unsubscribe messages and Agent-side subscription cancellation.
- Added client SDK connection health reporting and explicit reconnect support.
- Added configuration-driven Linux GPIO/PWM motor mapping.
- Replaced narrow samples with a professional all-in-one Avalonia GUI sample for device profiles, GPIO, PWM, motors, I2C, camera control, and Agent config updates.
- Added shared Agent config contracts plus durable config update protocol with restart-required semantics.
- Added I2C register access and camera control/status contracts with remote client and mock Agent support.
- Added real Linux I2C register access for the `linux-gpio` backend using Linux I2C device files.

## Next Phases

- Expand client-agent integration coverage against a real WebSocket listener.
- Verify and document the Linux systemd installation flow.
- Add authentication and authorization to the WebSocket transport.
- Add provisioning implementations for WiFi and Bluetooth setup.
- Add camera stream protocol and transport support.
- Implement a real Linux camera hardware provider.
- Add ADRs for protocol versioning and transport replacement.
