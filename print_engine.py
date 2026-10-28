import os
import math
import io
import time
import threading
from typing import List, Dict, Any, Optional, Callable
from PIL import Image, ImageDraw, ImageWin
import pypdfium2 as pdfium
import win32print
import win32gui
import win32ui
import win32con

# Global re-entrant lock to make pdfium.dll 100% thread-safe
PDFIUM_LOCK = threading.RLock()

# Paper size mappings
PAPER_SIZES = {
    "A4": win32con.DMPAPER_A4,
    "Letter": win32con.DMPAPER_LETTER,
    "Legal": win32con.DMPAPER_LEGAL,
}

# Duplex mappings
DUPLEX_MODES = {
    "simplex": win32con.DMDUP_SIMPLEX,
    "vertical": win32con.DMDUP_VERTICAL,      # Long edge (booklet / standard flip)
    "horizontal": win32con.DMDUP_HORIZONTAL,  # Short edge (calendar flip)
}

# Color modes
COLOR_MODES = {
    "bw": win32con.DMCOLOR_MONOCHROME,
    "color": win32con.DMCOLOR_COLOR,
}


def is_virtual_printer(name: str) -> bool:
    """Check if a printer is a virtual / software printer."""
    n = name.lower()
    return any(v in n for v in ["pdf", "onenote", "xps", "fax", "writer", "document"])


def get_printer_status(printer_name: str) -> Dict[str, Any]:
    """Query live Windows Spooler status for a given printer."""
    try:
        h = win32print.OpenPrinter(printer_name)
        info = win32print.GetPrinter(h, 2)
        win32print.ClosePrinter(h)

        st = info.get("Status", 0)
        attrs = info.get("Attributes", 0)
        jobs = info.get("cJobs", 0)
        is_virt = is_virtual_printer(printer_name)

        # Work offline attribute or status offline flag
        if (attrs & win32print.PRINTER_ATTRIBUTE_WORK_OFFLINE) or (st & 0x00000080):
            return {
                "state": "offline",
                "label": "অফলাইন (কানেক্টেড নয়)",
                "short_label": "অফলাইন",
                "css": "offline",
                "icon": "fa-circle-xmark",
                "jobs": jobs,
                "is_ready": False,
                "is_virtual": is_virt
            }
        if st & 0x00000008:
            return {
                "state": "paper_jam",
                "label": "কাগজ আটকে আছে (Paper Jam)",
                "short_label": "জ্যাম",
                "css": "warning",
                "icon": "fa-triangle-exclamation",
                "jobs": jobs,
                "is_ready": False,
                "is_virtual": is_virt
            }
        if st & 0x00000010:
            return {
                "state": "paper_out",
                "label": "কাগজ নেই (Paper Out)",
                "short_label": "কাগজ নেই",
                "css": "warning",
                "icon": "fa-triangle-exclamation",
                "jobs": jobs,
                "is_ready": False,
                "is_virtual": is_virt
            }
        if st & 0x00400000:
            return {
                "state": "door_open",
                "label": "কভার খোলা (Door Open)",
                "short_label": "কভার খোলা",
                "css": "warning",
                "icon": "fa-triangle-exclamation",
                "jobs": jobs,
                "is_ready": False,
                "is_virtual": is_virt
            }
        if st & 0x00000001:
            return {
                "state": "paused",
                "label": "পজ করা আছে (Paused)",
                "short_label": "পজড",
                "css": "warning",
                "icon": "fa-pause",
                "jobs": jobs,
                "is_ready": False,
                "is_virtual": is_virt
            }
        if st & (0x00000200 | 0x00000400 | 0x00004000):
            return {
                "state": "busy",
                "label": f"ব্যস্ত ({jobs}টি কাজ চলছে)",
                "short_label": "ব্যস্ত",
                "css": "busy",
                "icon": "fa-arrows-rotate fa-spin",
                "jobs": jobs,
                "is_ready": True,
                "is_virtual": is_virt
            }
        if st & 0x00000002:
            return {
                "state": "error",
                "label": "প্রিন্টারে সমস্যা (Error)",
                "short_label": "ত্রুটি",
                "css": "danger",
                "icon": "fa-circle-exclamation",
                "jobs": jobs,
                "is_ready": False,
                "is_virtual": is_virt
            }
        if jobs > 0:
            return {
                "state": "printing",
                "label": f"প্রিন্ট চলছে ({jobs}টি কিউ)",
                "short_label": f"কিউ ({jobs})",
                "css": "busy",
                "icon": "fa-spinner fa-spin",
                "jobs": jobs,
                "is_ready": True,
                "is_virtual": is_virt
            }

        return {
            "state": "ready",
            "label": "রেডি (প্রিন্টের জন্য প্রস্তুত)",
            "short_label": "রেডি",
            "css": "ready",
            "icon": "fa-circle-check",
            "jobs": 0,
            "is_ready": True,
            "is_virtual": is_virt
        }
    except Exception as e:
        return {
            "state": "unknown",
            "label": "স্ট্যাটাস পাওয়া যায়নি",
            "short_label": "অজ্ঞাত",
            "css": "unknown",
            "icon": "fa-circle-question",
            "jobs": 0,
            "is_ready": False,
            "error": str(e),
            "is_virtual": is_virtual_printer(printer_name)
        }


def build_quality_options(max_dpi: int, hardware_dpis: List[int]) -> List[Dict[str, Any]]:
    """Build dynamically adapted quality tiers strictly bounded by this printer's hardware maximum."""
    candidate_set = set(hardware_dpis)
    candidate_set.add(max_dpi)

    if max_dpi >= 2400:
        candidate_set.update([2400, 1200, 600, 300, 150])
    elif max_dpi >= 1200:
        candidate_set.update([1200, 600, 300, 150])
    elif max_dpi >= 600:
        candidate_set.update([600, 300, 150])
    elif max_dpi >= 400:
        candidate_set.update([max_dpi, 300, 150])
    elif max_dpi >= 300:
        candidate_set.update([300, 150])
    else:
        candidate_set.update([max_dpi, 150])

    # Filter out anything higher than what THIS printer supports
    valid_dpis = sorted([d for d in candidate_set if d <= max_dpi and d >= 100], reverse=True)
    options = []
    for d in valid_dpis:
        is_max = (d == max_dpi)
        if is_max:
            if d >= 2400:
                tier_desc = "ফটো কোয়ালিটি"
            elif d >= 1200:
                tier_desc = "আল্ট্রা কোয়ালিটি"
            elif d >= 600:
                tier_desc = "হাই কোয়ালিটি"
            elif d >= 300:
                tier_desc = "স্ট্যান্ডার্ড কোয়ালিটি"
            else:
                tier_desc = "বেস্ট কোয়ালিটি"
            lbl = f"{d} DPI (এই প্রিন্টারের সর্বোচ্চ / {tier_desc}) ★"
        elif d >= 1200:
            lbl = f"{d} DPI (আল্ট্রা কোয়ালিটি / ফাইন)"
        elif d >= 600:
            lbl = f"{d} DPI (হাই কোয়ালিটি / স্পষ্ট)"
        elif d >= 300:
            lbl = f"{d} DPI (স্ট্যান্ডার্ড কোয়ালিটি)"
        else:
            lbl = f"{d} DPI (ড্রাফট / দ্রুত ও সাশ্রয়ী)"

        options.append({
            "dpi": d,
            "label": lbl,
            "is_default": is_max
        })
    return options


def list_printers() -> List[Dict[str, Any]]:
    """Enumerate Windows printers, query supported resolutions, live status, and identify capabilities."""
    printers_list = []
    default_printer = ""
    try:
        default_printer = win32print.GetDefaultPrinter()
    except Exception:
        pass

    enum_flags = win32print.PRINTER_ENUM_LOCAL | win32print.PRINTER_ENUM_CONNECTIONS
    for p in win32print.EnumPrinters(enum_flags):
        p_name = p[2]
        is_default = (p_name == default_printer)
        is_virt = is_virtual_printer(p_name)
        duplex_supported = False
        color_supported = False
        supported_dpis = []

        try:
            hprinter = win32print.OpenPrinter(p_name)
            pinfo = win32print.GetPrinter(hprinter, 2)
            devmode = pinfo.get('pDevMode')
            if devmode:
                if devmode.Duplex in (win32con.DMDUP_VERTICAL, win32con.DMDUP_HORIZONTAL) or (devmode.Fields & win32con.DM_DUPLEX):
                    duplex_supported = True
                if devmode.Color == win32con.DMCOLOR_COLOR or (devmode.Fields & win32con.DM_COLOR):
                    color_supported = True
            win32print.ClosePrinter(hprinter)
        except Exception:
            pass

        # Query supported hardware resolutions (DC_ENUMRESOLUTIONS = 13)
        try:
            res_list = win32print.DeviceCapabilities(p_name, '', 13)
            if res_list:
                for r in res_list:
                    if isinstance(r, dict) and 'xdpi' in r:
                        supported_dpis.append(int(r['xdpi']))
                    elif isinstance(r, tuple) and len(r) >= 1:
                        supported_dpis.append(int(r[0]))
        except Exception:
            pass

        supported_dpis = sorted(list(set([d for d in supported_dpis if d > 0])))
        if not supported_dpis:
            supported_dpis = [300, 600]

        max_dpi = max(supported_dpis)
        quality_options = build_quality_options(max_dpi, supported_dpis)
        status = get_printer_status(p_name)

        printers_list.append({
            "name": p_name,
            "is_default": is_default,
            "is_virtual": is_virt,
            "duplex_supported": duplex_supported,
            "color_supported": color_supported,
            "supported_dpis": supported_dpis,
            "max_dpi": max_dpi,
            "quality_options": quality_options,
            "status": status
        })

    # Sort order: physical printers first, then virtual; default printer first within group
    printers_list.sort(key=lambda x: (
        x["is_virtual"],
        not x["is_default"],
        x["name"]
    ))
    return printers_list


def get_pdf_info(pdf_path: str) -> Dict[str, Any]:
    """Get metadata for PDF: page count, dimensions, orientation."""
    with PDFIUM_LOCK:
        pdf = pdfium.PdfDocument(pdf_path)
        page_count = len(pdf)
        sample_pages = []
        for i in range(min(5, page_count)):
            page = pdf[i]
            w, h = page.get_size()
            sample_pages.append({
                "index": i,
                "width": round(w, 1),
                "height": round(h, 1),
                "is_portrait": h >= w
            })
        pdf.close()

    first_page_portrait = sample_pages[0]["is_portrait"] if sample_pages else True
    return {
        "page_count": page_count,
        "sample_pages": sample_pages,
        "is_mostly_portrait": first_page_portrait,
        "detected_orientation": "portrait" if first_page_portrait else "landscape"
    }


def parse_page_range(range_str: str, total_pages: int) -> List[int]:
    """Parse page range string like '1-3, 5, 8' into 0-indexed list of page numbers."""
    if not range_str or range_str.strip().lower() in ("all", "*", ""):
        return list(range(total_pages))

    clean = range_str.strip().lower()
    if clean == "odd":
        return [i for i in range(total_pages) if (i + 1) % 2 != 0]
    if clean == "even":
        return [i for i in range(total_pages) if (i + 1) % 2 == 0]

    pages = set()
    for part in clean.split(","):
        part = part.strip()
        if not part:
            continue
        if "-" in part:
            sub = part.split("-")
            if len(sub) == 2:
                try:
                    start = int(sub[0]) if sub[0] else 1
                    end = int(sub[1]) if sub[1] else total_pages
                    for p in range(max(1, start), min(total_pages, end) + 1):
                        pages.add(p - 1)
                except ValueError:
                    pass
        else:
            try:
                p = int(part)
                if 1 <= p <= total_pages:
                    pages.add(p - 1)
            except ValueError:
                pass

    res = sorted(list(pages))
    return res if res else list(range(total_pages))


def render_page_image(pdf: pdfium.PdfDocument, page_idx: int, scale: float = 2.0) -> Image.Image:
    """Render a single PDF page into PIL Image (must be called with PDFIUM_LOCK held)."""
    page = pdf[page_idx]
    pil_image = page.render(scale=scale).to_pil()
    return pil_image


def get_grid_dimensions(nup: int, is_landscape_sheet: bool) -> tuple[int, int]:
    """Determine (columns, rows) for N-up layout on a given sheet orientation."""
    if nup <= 1:
        return (1, 1)
    elif nup == 2:
        return (2, 1) if is_landscape_sheet else (1, 2)
    elif nup == 4:
        return (2, 2)
    elif nup == 6:
        return (3, 2) if is_landscape_sheet else (2, 3)
    elif nup == 9:
        return (3, 3)
    else:
        cols = math.ceil(math.sqrt(nup))
        rows = math.ceil(nup / cols)
        return (cols, rows)


def compose_sheet(
    page_images: List[Image.Image],
    sheet_w: int,
    sheet_h: int,
    nup: int = 1,
    margin_ratio: float = 0.03,
    draw_borders: bool = True,
    color_mode: str = "bw"
) -> Image.Image:
    """Compose 1 to N rendered page images onto a single sheet canvas."""
    sheet = Image.new("RGB", (sheet_w, sheet_h), "white")
    draw = ImageDraw.Draw(sheet)

    is_landscape = sheet_w > sheet_h
    cols, rows = get_grid_dimensions(nup, is_landscape)

    cell_w = sheet_w / cols
    cell_h = sheet_h / rows

    pad_x = cell_w * margin_ratio
    pad_y = cell_h * margin_ratio
    avail_w = cell_w - (pad_x * 2)
    avail_h = cell_h - (pad_y * 2)

    for idx, img in enumerate(page_images):
        if idx >= cols * rows:
            break
        col = idx % cols
        row = idx // cols

        x0 = col * cell_w + pad_x
        y0 = row * cell_h + pad_y

        img_w, img_h = img.size
        scale = min(avail_w / img_w, avail_h / img_h)
        target_w = max(1, int(img_w * scale))
        target_h = max(1, int(img_h * scale))

        resized = img.resize((target_w, target_h), Image.Resampling.LANCZOS)

        paste_x = int(x0 + (avail_w - target_w) / 2)
        paste_y = int(y0 + (avail_h - target_h) / 2)

        sheet.paste(resized, (paste_x, paste_y))

        if draw_borders and nup > 1:
            draw.rectangle(
                [paste_x, paste_y, paste_x + target_w - 1, paste_y + target_h - 1],
                outline="#94a3b8",
                width=max(1, int(sheet_w / 1800))
            )

    if color_mode == "bw":
        sheet = sheet.convert("L").convert("RGB")

    return sheet


def render_thumbnail(pdf_path: str, page_idx: int = 0, max_size: int = 240) -> bytes:
    """Render page thumbnail to JPEG bytes in a thread-safe manner."""
    with PDFIUM_LOCK:
        pdf = pdfium.PdfDocument(pdf_path)
        if page_idx >= len(pdf):
            page_idx = 0
        page = pdf[page_idx]
        w, h = page.get_size()
        scale = (max_size * 2) / max(w, h)
        img = page.render(scale=scale).to_pil()
        img.thumbnail((max_size, max_size), Image.Resampling.LANCZOS)
        pdf.close()

    buf = io.BytesIO()
    img.save(buf, format="JPEG", quality=85)
    return buf.getvalue()


def render_sheet_preview_image(
    pdf_path: str,
    page_indices: List[int],
    nup: int = 1,
    color_mode: str = "bw",
    orientation: str = "auto",
    preview_max_dim: int = 800
) -> bytes:
    """Render a preview of how a composite sheet will look with given settings."""
    images = []
    first_page_w = 595
    first_page_h = 842

    with PDFIUM_LOCK:
        pdf = pdfium.PdfDocument(pdf_path)
        total = len(pdf)
        valid_indices = [i for i in page_indices if 0 <= i < total][:nup]
        if valid_indices:
            first_page_w, first_page_h = pdf[valid_indices[0]].get_size()
        for idx in valid_indices:
            images.append(render_page_image(pdf, idx, scale=1.5))
        pdf.close()

    # Orientation auto-detection
    is_page_landscape = (first_page_w > first_page_h)
    if orientation == "portrait":
        is_landscape = False
    elif orientation == "landscape":
        is_landscape = True
    else:  # "auto": match original orientation
        if nup == 2:
            is_landscape = not is_page_landscape
        elif nup == 6:
            is_landscape = True
        else:
            is_landscape = is_page_landscape

    if is_landscape:
        sheet_w = preview_max_dim
        sheet_h = int(preview_max_dim / 1.4142)
    else:
        sheet_h = preview_max_dim
        sheet_w = int(preview_max_dim / 1.4142)

    composed = compose_sheet(
        page_images=images,
        sheet_w=sheet_w,
        sheet_h=sheet_h,
        nup=nup,
        margin_ratio=0.03,
        draw_borders=(nup > 1),
        color_mode=color_mode
    )

    buf = io.BytesIO()
    composed.save(buf, format="PNG")
    return buf.getvalue()


def execute_print_job(
    jobs: List[Dict[str, Any]],
    printer_name: str,
    global_settings: Dict[str, Any],
    progress_cb: Optional[Callable[[Dict[str, Any]], None]] = None,
    cancel_check: Optional[Callable[[], bool]] = None
) -> Dict[str, Any]:
    """
    Execute printing of queued PDF files silently to the target Windows printer.
    Applies resolution (DPI) and per-document automatic orientation matching the original PDF.
    """
    if not printer_name:
        printer_name = win32print.GetDefaultPrinter()

    hprinter = win32print.OpenPrinter(printer_name)
    base_devmode = win32print.GetPrinter(hprinter, 2)["pDevMode"]
    win32print.ClosePrinter(hprinter)

    # Global options
    g_paper = global_settings.get("paper", "A4")
    g_color = global_settings.get("color", "bw")
    g_duplex = global_settings.get("duplex", "simplex")
    g_nup = int(global_settings.get("nup", 1))
    g_orientation = global_settings.get("orientation", "auto")
    g_copies = int(global_settings.get("copies", 1))
    g_draw_borders = bool(global_settings.get("draw_borders", True))
    g_dpi = int(global_settings.get("dpi", 600))

    # Apply base DEVMODE settings
    base_devmode.PaperSize = PAPER_SIZES.get(g_paper, win32con.DMPAPER_A4)
    base_devmode.Color = COLOR_MODES.get(g_color, win32con.DMCOLOR_MONOCHROME)
    base_devmode.Duplex = DUPLEX_MODES.get(g_duplex, win32con.DMDUP_SIMPLEX)
    base_devmode.Copies = max(1, g_copies)
    base_devmode.PrintQuality = g_dpi
    base_devmode.YResolution = g_dpi

    field_mask = (
        win32con.DM_PAPERSIZE |
        win32con.DM_COLOR |
        win32con.DM_DUPLEX |
        win32con.DM_ORIENTATION |
        win32con.DM_COPIES |
        win32con.DM_PRINTQUALITY |
        win32con.DM_YRESOLUTION
    )
    base_devmode.Fields |= field_mask

    # Calculate total sheets for overall progress
    total_sheets_all = 0
    job_plans = []

    for job in jobs:
        pdf_path = job["path"]
        with PDFIUM_LOCK:
            pdf = pdfium.PdfDocument(pdf_path)
            total_p = len(pdf)
            # Detect orientation from first page
            first_page_w, first_page_h = (595, 842)
            if total_p > 0:
                first_page_w, first_page_h = pdf[0].get_size()
            pdf.close()

        is_page_landscape = (first_page_w > first_page_h)
        f_nup = int(job.get("nup", g_nup))
        f_range = job.get("page_range", "all")
        pages_to_print = parse_page_range(f_range, total_p)
        sheets_needed = math.ceil(len(pages_to_print) / f_nup) if pages_to_print else 0
        total_sheets_all += sheets_needed

        job_plans.append({
            "job": job,
            "total_pages": total_p,
            "pages_to_print": pages_to_print,
            "sheets_needed": sheets_needed,
            "nup": f_nup,
            "is_page_landscape": is_page_landscape
        })

    if total_sheets_all == 0:
        return {"success": True, "message": "No pages to print.", "sheets_printed": 0}

    is_pdf_virtual = is_virtual_printer(printer_name)
    printed_sheets_count = 0
    total_docs = len(job_plans)

    # Process each document
    for doc_idx, plan in enumerate(job_plans):
        if cancel_check and cancel_check():
            break

        job = plan["job"]
        pdf_path = job["path"]
        filename = job.get("name", os.path.basename(pdf_path))
        pages_to_print = plan["pages_to_print"]
        f_nup = plan["nup"]
        f_color = job.get("color", g_color)
        f_draw_borders = bool(job.get("draw_borders", g_draw_borders))
        f_orient = job.get("orientation", g_orientation)
        is_page_landscape = plan["is_page_landscape"]

        if not pages_to_print:
            continue

        # Determine exact orientation for this document
        if f_orient == "portrait":
            doc_is_landscape = False
        elif f_orient == "landscape":
            doc_is_landscape = True
        else:  # "auto": match original PDF orientation
            if f_nup == 2:
                # 2 portrait pages fit best on landscape sheet
                doc_is_landscape = not is_page_landscape
            elif f_nup == 6:
                doc_is_landscape = True
            else:
                doc_is_landscape = is_page_landscape

        # Clone devmode for this document and set exact orientation
        hprinter = win32print.OpenPrinter(printer_name)
        doc_devmode = win32print.GetPrinter(hprinter, 2)["pDevMode"]
        win32print.ClosePrinter(hprinter)

        doc_devmode.PaperSize = base_devmode.PaperSize
        doc_devmode.Color = COLOR_MODES.get(f_color, base_devmode.Color)
        doc_devmode.Duplex = base_devmode.Duplex
        doc_devmode.Copies = base_devmode.Copies
        doc_devmode.PrintQuality = g_dpi
        doc_devmode.YResolution = g_dpi
        doc_devmode.Orientation = win32con.DMORIENT_LANDSCAPE if doc_is_landscape else win32con.DMORIENT_PORTRAIT
        doc_devmode.Fields |= field_mask

        # Create DC specifically for this document
        hdc = win32gui.CreateDC("WINSPOOL", printer_name, doc_devmode)
        dc = win32ui.CreateDCFromHandle(hdc)

        doc_title = f"Print Studio - {filename}"

        try:
            if is_pdf_virtual:
                desktop = os.path.join(os.path.expanduser("~"), "Desktop")
                out_dir = os.path.join(desktop, "Print_Studio_Output")
                os.makedirs(out_dir, exist_ok=True)
                clean_name = os.path.splitext(filename)[0]
                out_file = os.path.join(out_dir, f"{clean_name}_printed_{int(time.time())}.pdf")
                dc.StartDoc(doc_title, out_file)
            else:
                dc.StartDoc(doc_title)

            dc_w = dc.GetDeviceCaps(win32con.HORZRES)
            dc_h = dc.GetDeviceCaps(win32con.VERTRES)

            # High-res canvas matching target DPI
            is_land = (dc_w > dc_h)
            if g_dpi >= 1200:
                render_scale = 5.5
                max_long, max_short = 4960, 3508
            elif g_dpi >= 600:
                render_scale = 4.167
                max_long, max_short = 3508, 2480
            elif g_dpi >= 400:
                render_scale = 3.333
                max_long, max_short = 2976, 2102
            elif g_dpi >= 300:
                render_scale = 2.5
                max_long, max_short = 2480, 1754
            else:
                render_scale = 1.5
                max_long, max_short = 1754, 1240

            target_cw = max_long if is_land else max_short
            target_ch = max_short if is_land else max_long
            comp_w = min(dc_w, target_cw)
            comp_h = min(dc_h, target_ch)

            total_doc_sheets = plan["sheets_needed"]
            sheet_num = 0

            with PDFIUM_LOCK:
                pdf = pdfium.PdfDocument(pdf_path)

                for i in range(0, len(pages_to_print), f_nup):
                    if cancel_check and cancel_check():
                        break

                    sheet_num += 1
                    printed_sheets_count += 1
                    chunk_page_indices = pages_to_print[i:i + f_nup]

                    if progress_cb:
                        progress_cb({
                            "doc_index": doc_idx + 1,
                            "total_docs": total_docs,
                            "filename": filename,
                            "doc_sheet": sheet_num,
                            "doc_total_sheets": total_doc_sheets,
                            "overall_sheet": printed_sheets_count,
                            "overall_total_sheets": total_sheets_all,
                            "percent": int((printed_sheets_count / total_sheets_all) * 100)
                        })

                    chunk_images = []
                    for p_idx in chunk_page_indices:
                        chunk_images.append(render_page_image(pdf, p_idx, scale=render_scale))

                    sheet_img = compose_sheet(
                        page_images=chunk_images,
                        sheet_w=comp_w,
                        sheet_h=comp_h,
                        nup=f_nup,
                        margin_ratio=0.03,
                        draw_borders=f_draw_borders,
                        color_mode=f_color
                    )

                    dc.StartPage()
                    dib = ImageWin.Dib(sheet_img)
                    dib.draw(hdc, (0, 0, dc_w, dc_h))
                    dc.EndPage()

                pdf.close()

            dc.EndDoc()

        finally:
            try:
                dc.DeleteDC()
            except Exception:
                pass

    return {
        "success": True,
        "total_docs": total_docs,
        "sheets_printed": printed_sheets_count,
        "cancelled": bool(cancel_check and cancel_check())
    }
