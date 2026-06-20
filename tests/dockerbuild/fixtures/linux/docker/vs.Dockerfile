# Test fixture - chain ROOT (stands in for the real vs17 base image). Deliberately tiny.
FROM alpine:3.20

# Per-image context: the sentinel content is unique to this layer's context dir (proves context isolation).
COPY sentinel.txt /ctx.txt
RUN echo vs-layer > /marker-vs.txt
