"""Independent DXF check with ezdxf: python3 tools/check_dxf.py file.dxf"""
import sys
from collections import Counter
import ezdxf
from ezdxf import recover

doc, auditor = recover.readfile(sys.argv[1])
print("DXF version:", doc.dxfversion)
print("audit errors:", len(auditor.errors), "fixes:", len(auditor.fixes))
msp = doc.modelspace()
print("entities by layer:", dict(Counter((e.dxftype(), e.dxf.layer) for e in msp)))
xs = [v for e in msp.query("LINE") for v in (e.dxf.start.x, e.dxf.end.x)]
ys = [v for e in msp.query("LINE") for v in (e.dxf.start.y, e.dxf.end.y)]
print("LINE extents x: %.4f .. %.4f  y: %.4f .. %.4f" % (min(xs), max(xs), min(ys), max(ys)))
sys.exit(1 if auditor.has_errors else 0)
