import os
import pickle as pickle_tts
import warnings
from typing import Any, Callable, Dict, Union

import fsspec
import torch

from TTS.utils.generic_utils import get_user_data_dir


class RenamingUnpickler(pickle_tts.Unpickler):
    """Overload default pickler to solve module renaming problem"""

    def find_class(self, module, name):
        return super().find_class(module.replace("mozilla_voice_tts", "TTS"), name)


class AttrDict(dict):
    """A custom dict which converts dict keys
    to class attributes"""

    def __init__(self, *args, **kwargs):
        super().__init__(*args, **kwargs)
        self.__dict__ = self


def load_fsspec(
    path: str,
    map_location: Union[str, Callable, torch.device, Dict[Union[str, torch.device], Union[str, torch.device]]] = None,
    cache: bool = True,
    **kwargs,
) -> Any:
    """Like torch.load but can load from other locations (e.g. s3:// , gs://).

    Args:
        path: Any path or url supported by fsspec.
        map_location: torch.device or str.
        cache: If True, cache a remote file locally for subsequent calls. It is cached under `get_user_data_dir()/tts_cache`. Defaults to True.
        **kwargs: Keyword arguments forwarded to torch.load.

    Returns:
        Object stored in path.
    """

    def _torch_load_with_compat(f):
        try:
            return torch.load(f, map_location=map_location, **kwargs)
        except pickle_tts.UnpicklingError as exc:
            # PyTorch 2.6+ defaults `weights_only=True`, which can break loading older checkpoints
            # that include pickled objects (e.g., configs) from trusted sources.
            if "Weights only load failed" not in str(exc) or "weights_only" in kwargs:
                raise
            if hasattr(f, "seek"):
                try:
                    f.seek(0)
                except Exception:
                    pass
            warnings.warn(
                "PyTorch weights-only load failed; retrying checkpoint load with weights_only=False. "
                "Do this only if you trust the source of the checkpoint.",
                UserWarning,
            )
            try:
                return torch.load(f, map_location=map_location, weights_only=False, **kwargs)
            except TypeError:
                # Older torch versions may not support the weights_only kwarg.
                raise exc

    is_local = os.path.isdir(path) or os.path.isfile(path)
    if cache and not is_local:
        with fsspec.open(
            f"filecache::{path}",
            filecache={"cache_storage": str(get_user_data_dir("tts_cache"))},
            mode="rb",
        ) as f:
            return _torch_load_with_compat(f)
    else:
        with fsspec.open(path, "rb") as f:
            return _torch_load_with_compat(f)


def load_checkpoint(
    model, checkpoint_path, use_cuda=False, eval=False, cache=False
):  # pylint: disable=redefined-builtin
    try:
        state = load_fsspec(checkpoint_path, map_location=torch.device("cpu"), cache=cache)
    except ModuleNotFoundError:
        pickle_tts.Unpickler = RenamingUnpickler
        state = load_fsspec(checkpoint_path, map_location=torch.device("cpu"), pickle_module=pickle_tts, cache=cache)
    model.load_state_dict(state["model"])
    if use_cuda:
        model.cuda()
    if eval:
        model.eval()
    return model, state
