# escape=`

# Test fixture - chain ROOT (stands in for the real vs17 base image). Deliberately tiny.
# The OS tag is delivered via the WINDOWS_VERSION build-arg by DockerBuild.ps1 and folded into the content hash.
ARG WINDOWS_VERSION=ltsc2025
FROM mcr.microsoft.com/windows/nanoserver:${WINDOWS_VERSION}

# Per-image context: the sentinel content is unique to this layer's context dir (proves context isolation).
COPY sentinel.txt C:\ctx.txt
RUN echo vs-layer> C:\marker-vs.txt
