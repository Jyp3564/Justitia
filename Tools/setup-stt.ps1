$ErrorActionPreference='Stop'
$destination=Join-Path $PSScriptRoot 'Whisper'
New-Item -ItemType Directory -Force $destination | Out-Null
Invoke-WebRequest 'https://github.com/ggml-org/whisper.cpp/releases/download/v1.9.2/whisper-bin-x64.zip' -OutFile (Join-Path $destination 'runtime.zip')
Expand-Archive -LiteralPath (Join-Path $destination 'runtime.zip') -DestinationPath $destination -Force
$model=Join-Path $destination 'ggml-small-q5_1.bin'
Invoke-WebRequest 'https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small-q5_1.bin' -OutFile $model
if((Get-FileHash $model -Algorithm SHA256).Hash -ne 'AE85E4A935D7A567BD102FE55AFC16BB595BDB618E11B2FC7591BC08120411BB'){throw 'Whisper model checksum mismatch.'}
Invoke-WebRequest 'https://raw.githubusercontent.com/ggml-org/whisper.cpp/v1.9.2/LICENSE' -OutFile (Join-Path $destination 'LICENSE-whisper.txt')
Invoke-WebRequest 'https://raw.githubusercontent.com/openai/whisper/main/LICENSE' -OutFile (Join-Path $destination 'LICENSE-model.txt')
Write-Output 'Korean STT runtime and model installed.'
