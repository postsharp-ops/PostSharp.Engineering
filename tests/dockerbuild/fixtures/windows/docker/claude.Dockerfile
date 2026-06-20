# escape=`

# Test fixture - chain LEAF (stands in for the real claude image). Builds FROM the resolved parent (build).
ARG BASE_IMAGE=build.Dockerfile
FROM ${BASE_IMAGE}

COPY sentinel.txt C:\ctx.txt
RUN echo claude-layer> C:\marker-claude.txt
