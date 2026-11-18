# Security policy

This repository is a discovery foundation, with no supported production release.
Do not deploy the research spike as an authorization service. The upstream
reference interpreter is not production-ready.

For DogwoodSharp defects, use GitHub private vulnerability reporting if enabled
on this repository. If unavailable, contact the repository owner privately to
arrange a channel before sharing sensitive details; no response SLA is promised.
Do not post exploit details or secrets in a public issue. Upstream Dogwood issues
should follow its own SECURITY.md and AWS/Amazon Security reporting channel.

See [threat model](docs/architecture/threat-model.md) for proposed contracts and
limitations. Provider networking and experimental arithmetic must remain off.

The D2-enabled native image has a process-lifetime residency contract.
Native consumers must hold the image loaded and free every response through
the same image. Plugin unload and hot reload do not reclaim native state.
See the [native ownership and hosting contract](docs/architecture/native-interop.md#d2-image-lifetime).
