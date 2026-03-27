$ErrorActionPreference = "Stop"

$pyVersion = py -3.11 -c "import sys; print(f'{sys.version_info.major}.{sys.version_info.minor}')"
if ($pyVersion.Trim() -ne "3.11") {
    throw "Python 3.11 is required. Install it and rerun setup.ps1."
}

py -3.11 -m venv .venv
.\.venv\Scripts\Activate.ps1

python -m pip install --upgrade pip
python -m pip install -r requirements.txt

# Force NVIDIA CUDA-enabled PyTorch wheels into this venv.
python -m pip install --upgrade --force-reinstall torch torchvision torchaudio --index-url https://download.pytorch.org/whl/cu124

# Install app deps after torch so TTS uses the CUDA build.
python -m pip install TTS sounddevice

# Verify interpreter + CUDA visibility.
python -c "import sys, torch; print('python=', sys.version); print('torch=', torch.__version__); print('cuda_available=', torch.cuda.is_available()); print('cuda_runtime=', torch.version.cuda); print('device=', torch.cuda.get_device_name(0) if torch.cuda.is_available() else 'cpu')"
