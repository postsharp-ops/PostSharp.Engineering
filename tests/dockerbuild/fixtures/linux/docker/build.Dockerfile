# Test fixture - middle of the chain. Builds FROM the resolved parent (vs); DockerBuild.ps1 rewrites the ARG
# default to the parent's content-hash tag via --build-arg BASE_IMAGE. The file name carries no prefix.
ARG BASE_IMAGE=vs.Dockerfile
FROM ${BASE_IMAGE}

COPY sentinel.txt /ctx.txt
RUN echo build-layer > /marker-build.txt
