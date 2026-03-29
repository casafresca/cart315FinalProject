import pathlib
import soundfile as sf
wav_dir = pathlib.Path('wavs')
print('wavs exists', wav_dir.exists(), 'files', [p.name for p in wav_dir.iterdir()])
for p in wav_dir.iterdir():
    st = p.stat()
    print(p.name, st.st_size, 'bytes')
    try:
        info = sf.info(str(p))
        print(' info', info)
        data, sr = sf.read(str(p))
        print(' read ok', data.shape, sr, data.dtype)
    except Exception as e:
        print(' read failed', e)
