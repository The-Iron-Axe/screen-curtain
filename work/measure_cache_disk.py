from pathlib import Path
import ctypes, json, sys

root=Path(sys.argv[1]).resolve()
kernel=ctypes.WinDLL('kernel32',use_last_error=True)
get_size=kernel.GetCompressedFileSizeW; get_size.argtypes=[ctypes.c_wchar_p,ctypes.POINTER(ctypes.c_uint32)]; get_size.restype=ctypes.c_uint32
disk=kernel.GetDiskFreeSpaceW; disk.argtypes=[ctypes.c_wchar_p,ctypes.POINTER(ctypes.c_uint32),ctypes.POINTER(ctypes.c_uint32),ctypes.POINTER(ctypes.c_uint32),ctypes.POINTER(ctypes.c_uint32)]
sectors=ctypes.c_uint32(); bytes_per_sector=ctypes.c_uint32(); free=ctypes.c_uint32(); total=ctypes.c_uint32()
if not disk(root.anchor,ctypes.byref(sectors),ctypes.byref(bytes_per_sector),ctypes.byref(free),ctypes.byref(total)):
    raise ctypes.WinError(ctypes.get_last_error())
cluster=sectors.value*bytes_per_sector.value
logical=compressed=allocated=count=0
for file in root.rglob('*'):
    if not file.is_file(): continue
    high=ctypes.c_uint32(); ctypes.set_last_error(0); low=get_size(str(file),ctypes.byref(high))
    if low==0xffffffff and ctypes.get_last_error(): raise ctypes.WinError(ctypes.get_last_error())
    size=(high.value<<32)|low; count+=1; logical+=file.stat().st_size; compressed+=size
    allocated+=((size+cluster-1)//cluster)*cluster
print(json.dumps({'path':str(root),'files':count,'logical_bytes':logical,'compressed_bytes':compressed,'estimated_allocated_bytes':allocated,'cluster_bytes':cluster},ensure_ascii=False))
