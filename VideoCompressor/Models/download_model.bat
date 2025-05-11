@echo off
set MODEL_URL=https://alphacephei.com/vosk/models/vosk-model-small-en-us-0.15.zip

echo Downloading Vosk model...

:: Download the ZIP to the current directory (Models/)
powershell -Command "Invoke-WebRequest -Uri '%MODEL_URL%' -OutFile 'model.zip'"

:: Unzip here (Models/) and delete the ZIP
powershell -Command "Expand-Archive -Path 'model.zip' -DestinationPath ."
if exist "model.zip" del "model.zip"

echo Model downloaded to: %cd%