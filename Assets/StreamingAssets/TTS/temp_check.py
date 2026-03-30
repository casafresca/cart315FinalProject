import torch
print('torch version:', torch.__version__)
print('cuda available:', torch.cuda.is_available())
print('cuda version:', torch.version.cuda)
print('device count:', torch.cuda.device_count())
if hasattr(torch.cuda, 'get_arch_list'):
    print('arch list:', torch.cuda.get_arch_list())
else:
    print('arch list: n/a')