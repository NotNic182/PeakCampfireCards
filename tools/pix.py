"""pix.py <png> <x> <y>  -> prints r g b at that pixel (original resolution)"""
import sys
from PIL import Image
im=Image.open(sys.argv[1]).convert('RGB'); x,y=int(sys.argv[2]),int(sys.argv[3]); print(*im.getpixel((x,y)))
