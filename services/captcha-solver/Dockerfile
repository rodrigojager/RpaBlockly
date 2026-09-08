# O build usa a raiz do repositório como contexto:
# docker build -f services/captcha-solver/Dockerfile -t captcha-solver .
FROM python:3.12-slim@sha256:78387bc3881b8273120a12ebe6c1ab22b018ccc2c9adf565ae1ac9b536e184ea

ARG CAPTCHA_WHISPER_MODEL=base
ARG CAPTCHA_WHISPER_REVISION=ebe41f70d5b6dfa9166e2c581c45c9c0cfc57b66
ARG CAPTCHA_PROVISION_WHISPER=1

WORKDIR /app
COPY services/captcha-solver/requirements.lock ./requirements.lock
RUN python -m pip install --no-cache-dir --disable-pip-version-check -r requirements.lock

COPY services/captcha-solver/app.py ./app.py
COPY services/captcha-solver/captcha_solver ./captcha_solver
COPY services/captcha-solver/provision_models.py ./provision_models.py
COPY src/RpaFlow.Playwright/V2/Captcha/captcha-models.manifest.json /app/source-models/captcha-models.manifest.json
COPY src/RpaFlow.Playwright/V2/Captcha/hcaptcha-models.manifest.json /app/source-models/hcaptcha-models.manifest.json
COPY src/RpaFlow.Playwright/V2/Captcha/hcaptcha-models.LICENSE.txt /app/source-models/hcaptcha-models.LICENSE.txt
COPY src/RpaFlow.Playwright/V2/Captcha/ocr-charset.json /app/source-models/ocr-charset.json

RUN mkdir -p /app/models/hcaptcha /opt/huggingface && \
    python provision_models.py \
      --manifest /app/source-models/captcha-models.manifest.json \
      --destination /app/models \
      --charset-source /app/source-models/ocr-charset.json \
      --hcaptcha-manifest /app/source-models/hcaptcha-models.manifest.json \
      --hcaptcha-destination /app/models/hcaptcha \
      --hcaptcha-license /app/source-models/hcaptcha-models.LICENSE.txt && \
    cp /app/source-models/hcaptcha-models.manifest.json /app/models/hcaptcha/hcaptcha-models.manifest.json && \
    if [ "$CAPTCHA_PROVISION_WHISPER" = "1" ]; then \
      HF_HOME=/opt/huggingface python provision_models.py \
        --manifest /app/source-models/captcha-models.manifest.json \
        --destination /app/models \
        --charset-source /app/source-models/ocr-charset.json \
        --whisper-model "$CAPTCHA_WHISPER_MODEL" \
        --whisper-revision "$CAPTCHA_WHISPER_REVISION"; \
    fi && \
    useradd --create-home --uid 10001 appuser && \
    chown -R appuser:appuser /app /opt/huggingface

ENV CAPTCHA_WHISPER_MODEL=${CAPTCHA_WHISPER_MODEL} \
    CAPTCHA_WHISPER_REVISION=${CAPTCHA_WHISPER_REVISION} \
    CAPTCHA_OCR_MODEL_PATH=/app/models/common.onnx \
    CAPTCHA_OCR_CHARSET_PATH=/app/models/ocr-charset.json \
    CAPTCHA_OCR_MANIFEST_PATH=/app/models/captcha-models.manifest.json \
    CAPTCHA_HCAPTCHA_MODEL_DIR=/app/models/hcaptcha \
    CAPTCHA_HCAPTCHA_MANIFEST_PATH=/app/models/hcaptcha/hcaptcha-models.manifest.json \
    CAPTCHA_MAX_AUDIO_BYTES=5242880 \
    HF_HOME=/opt/huggingface \
    PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1

EXPOSE 8855
USER appuser
HEALTHCHECK --interval=30s --timeout=5s --retries=3 CMD ["python", "-c", "import urllib.request; urllib.request.urlopen('http://127.0.0.1:8855/ready', timeout=3).read()"]
CMD ["uvicorn", "app:app", "--host", "0.0.0.0", "--port", "8855", "--limit-concurrency", "4"]
