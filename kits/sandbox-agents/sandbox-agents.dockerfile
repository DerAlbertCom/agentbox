FROM scratch
COPY --chown=1000:1000 files/home/ /home/agent/
COPY --chown=0:0 files/opt/ /opt/
COPY --chown=1000:1000 --chmod=0755 files/home/.local/bin/ /home/agent/.local/bin/
COPY --chown=1000:1000 --chmod=0755 files/opt/sandbox/startup/dotnetup /home/agent/.local/bin/dotnetup
