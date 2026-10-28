import os
import sys
import uuid
import tempfile
import threading
import time
import webbrowser
import traceback
from flask import Flask, render_template, request, jsonify, send_file, Response
import print_engine
import settings_manager

# PyInstaller bundle support
if getattr(sys, "frozen", False):
    BUNDLE_DIR = getattr(sys, "_MEIPASS", os.path.dirname(sys.executable))
    TEMPLATE_DIR = os.path.join(BUNDLE_DIR, "templates")
    STATIC_DIR = os.path.join(BUNDLE_DIR, "static")
    app = Flask(__name__, template_folder=TEMPLATE_DIR, static_folder=STATIC_DIR)
else:
    BASE_DIR = os.path.dirname(os.path.abspath(__file__))
    app = Flask(__name__)

# Temporary upload workspace in system temp to keep USB / portable directory pristine
UPLOAD_DIR = os.path.join(tempfile.gettempdir(), "PrintStudio_Uploads")
THUMB_DIR = os.path.join(UPLOAD_DIR, "thumbs")
os.makedirs(UPLOAD_DIR, exist_ok=True)
os.makedirs(THUMB_DIR, exist_ok=True)

# In-memory store for files in the current print queue
file_queue = []

# Print jobs status tracking
print_jobs = {}


def format_size(bytes_num: int) -> str:
    """Format bytes into human-readable string."""
    if bytes_num < 1024:
        return f"{bytes_num} B"
    elif bytes_num < 1024 * 1024:
        return f"{bytes_num / 1024:.1f} KB"
    else:
        return f"{bytes_num / (1024 * 1024):.1f} MB"


@app.route("/")
def index():
    return render_template("index.html")


@app.route("/api/settings", methods=["GET", "POST"])
def handle_settings():
    """Get or save user configuration settings for true portability."""
    if request.method == "POST":
        data = request.json or {}
        settings_manager.save_settings(data)
        return jsonify({"success": True, "settings": settings_manager.load_settings()})
    return jsonify({"success": True, "settings": settings_manager.load_settings()})


@app.route("/api/printers", methods=["GET"])
def get_printers():
    """Return available printers and their capabilities."""
    try:
        printers = print_engine.list_printers()
        return jsonify({"success": True, "printers": printers})
    except Exception as e:
        traceback.print_exc()
        return jsonify({"success": False, "error": str(e)}), 500


@app.route("/api/printer_status", methods=["GET"])
def get_printer_status_route():
    """Return live status for a specific printer or all printers."""
    printer_name = request.args.get("printer", "").strip()
    try:
        if printer_name:
            status = print_engine.get_printer_status(printer_name)
            return jsonify({"success": True, "printer": printer_name, "status": status})
        else:
            printers = print_engine.list_printers()
            statuses = {p["name"]: p.get("status") for p in printers}
            return jsonify({"success": True, "statuses": statuses})
    except Exception as e:
        traceback.print_exc()
        return jsonify({"success": False, "error": str(e)}), 500


@app.route("/api/open_url", methods=["POST"])
def open_url():
    """Safely open external links in default web browser."""
    data = request.json or {}
    url = data.get("url", "").strip()
    if url.startswith("http://") or url.startswith("https://") or url.startswith("mailto:"):
        try:
            webbrowser.open(url)
            return jsonify({"success": True})
        except Exception as e:
            return jsonify({"success": False, "error": str(e)}), 500
    return jsonify({"success": False, "error": "Invalid URL"}), 400


@app.route("/api/upload", methods=["POST"])
def upload_files():
    """Handle drag-and-drop or file input upload of PDF files."""
    if "files" not in request.files:
        return jsonify({"success": False, "error": "No files uploaded."}), 400

    uploaded = request.files.getlist("files")
    added = []

    for file in uploaded:
        if not file.filename:
            continue
        if not file.filename.lower().endswith(".pdf"):
            continue

        file_id = str(uuid.uuid4())
        ext = os.path.splitext(file.filename)[1]
        save_name = f"{file_id}{ext}"
        save_path = os.path.join(UPLOAD_DIR, save_name)
        thumb_path = os.path.join(THUMB_DIR, f"{file_id}_0.jpg")
        file.save(save_path)

        try:
            info = print_engine.get_pdf_info(save_path)
            size_str = format_size(os.path.getsize(save_path))

            # Pre-generate thumbnail to disk so concurrent HTTP requests never hit PDFium
            try:
                thumb_bytes = print_engine.render_thumbnail(save_path, page_idx=0, max_size=240)
                with open(thumb_path, "wb") as tf:
                    tf.write(thumb_bytes)
            except Exception as te:
                print(f"Warning: Failed to cache thumbnail for {file.filename}: {te}")

            item = {
                "id": file_id,
                "filename": file.filename,
                "path": save_path,
                "thumb_path": thumb_path if os.path.exists(thumb_path) else None,
                "size_str": size_str,
                "page_count": info["page_count"],
                "is_mostly_portrait": info["is_mostly_portrait"],
                "detected_orientation": info.get("detected_orientation", "portrait"),
                "settings": {
                    "use_custom": False,
                    "page_range": "all",
                    "nup": 1,
                    "color": "bw",
                    "orientation": "auto",
                    "copies": 1
                }
            }
            file_queue.append(item)
            added.append(item)
        except Exception as e:
            traceback.print_exc()
            if os.path.exists(save_path):
                os.remove(save_path)
            return jsonify({"success": False, "error": f"Failed to process '{file.filename}': {str(e)}"}), 400

    return jsonify({"success": True, "added": added, "total": len(file_queue)})


@app.route("/api/queue", methods=["GET"])
def get_queue():
    """Return current print queue."""
    return jsonify({"success": True, "queue": file_queue})


@app.route("/api/queue/<file_id>", methods=["DELETE"])
def remove_from_queue(file_id):
    """Remove a file from the queue and disk."""
    global file_queue
    for i, item in enumerate(file_queue):
        if item["id"] == file_id:
            try:
                if os.path.exists(item["path"]):
                    os.remove(item["path"])
                thumb_path = os.path.join(THUMB_DIR, f"{file_id}_0.jpg")
                if os.path.exists(thumb_path):
                    os.remove(thumb_path)
            except Exception:
                pass
            file_queue.pop(i)
            return jsonify({"success": True, "total": len(file_queue)})
    return jsonify({"success": False, "error": "File not found"}), 404


@app.route("/api/queue/clear", methods=["POST"])
def clear_queue():
    """Clear all files from the queue and delete temporary uploads."""
    global file_queue
    for item in file_queue:
        try:
            if os.path.exists(item["path"]):
                os.remove(item["path"])
            thumb_path = os.path.join(THUMB_DIR, f"{item['id']}_0.jpg")
            if os.path.exists(thumb_path):
                os.remove(thumb_path)
        except Exception:
            pass
    file_queue = []
    return jsonify({"success": True, "total": 0})


@app.route("/api/queue/reorder", methods=["POST"])
def reorder_queue():
    """Update order of files based on array of IDs."""
    global file_queue
    data = request.json or {}
    new_order_ids = data.get("ids", [])
    id_map = {item["id"]: item for item in file_queue}

    reordered = []
    for fid in new_order_ids:
        if fid in id_map:
            reordered.append(id_map[fid])
    for item in file_queue:
        if item["id"] not in new_order_ids:
            reordered.append(item)

    file_queue = reordered
    return jsonify({"success": True, "queue": file_queue})


@app.route("/api/queue/update/<file_id>", methods=["POST"])
def update_item_settings(file_id):
    """Update custom settings for a specific file in the queue."""
    data = request.json or {}
    for item in file_queue:
        if item["id"] == file_id:
            item["settings"].update(data)
            return jsonify({"success": True, "item": item})
    return jsonify({"success": False, "error": "File not found"}), 404


@app.route("/api/thumbnail/<file_id>/<int:page_idx>", methods=["GET"])
def get_thumbnail(file_id, page_idx):
    """Return rendered page thumbnail, serving from disk cache if available."""
    thumb_path = os.path.join(THUMB_DIR, f"{file_id}_{page_idx}.jpg")
    if os.path.exists(thumb_path):
        return send_file(thumb_path, mimetype="image/jpeg")

    for item in file_queue:
        if item["id"] == file_id:
            try:
                thumb_bytes = print_engine.render_thumbnail(item["path"], page_idx=page_idx, max_size=240)
                try:
                    with open(thumb_path, "wb") as f:
                        f.write(thumb_bytes)
                except Exception:
                    pass
                return Response(thumb_bytes, mimetype="image/jpeg")
            except Exception as e:
                traceback.print_exc()
                return jsonify({"error": str(e)}), 500
    return jsonify({"error": "File not found"}), 404


@app.route("/api/preview_sheet", methods=["POST"])
def preview_sheet():
    """Return composite preview image of a sheet with specified N-up and color."""
    data = request.json or {}
    file_id = data.get("file_id")
    nup = int(data.get("nup", 1))
    color_mode = data.get("color", "bw")
    orientation = data.get("orientation", "auto")

    for item in file_queue:
        if item["id"] == file_id:
            try:
                preview_png = print_engine.render_sheet_preview_image(
                    pdf_path=item["path"],
                    page_indices=list(range(min(nup, item["page_count"]))),
                    nup=nup,
                    color_mode=color_mode,
                    orientation=orientation,
                    preview_max_dim=700
                )
                return Response(preview_png, mimetype="image/png")
            except Exception as e:
                traceback.print_exc()
                return jsonify({"error": str(e)}), 500
    return jsonify({"error": "File not found"}), 404


def run_print_worker(job_id: str, printer_name: str, global_settings: dict, jobs_data: list):
    """Background worker thread for silent printing."""
    job_info = print_jobs[job_id]
    job_info["status"] = "running"

    def progress_callback(progress_data):
        job_info["progress"] = progress_data

    def cancel_check():
        return job_info.get("cancel_requested", False)

    try:
        result = print_engine.execute_print_job(
            jobs=jobs_data,
            printer_name=printer_name,
            global_settings=global_settings,
            progress_cb=progress_callback,
            cancel_check=cancel_check
        )
        if result.get("cancelled"):
            job_info["status"] = "cancelled"
        else:
            job_info["status"] = "completed"
            job_info["result"] = result
    except Exception as e:
        traceback.print_exc()
        job_info["status"] = "error"
        job_info["error"] = str(e)


@app.route("/api/print", methods=["POST"])
def start_print():
    """Start printing all queued documents with global & per-file settings."""
    try:
        if not file_queue:
            return jsonify({"success": False, "error": "প্রিন্ট করার জন্য কোনো ফাইল নেই।"}), 400

        data = request.json or {}
        printer_name = data.get("printer")
        if not printer_name:
            return jsonify({"success": False, "error": "দয়া করে একটি প্রিন্টার সিলেক্ট করুন।"}), 400

        global_settings = data.get("global_settings", {
            "paper": "A4",
            "color": "bw",
            "duplex": "simplex",
            "nup": 1,
            "orientation": "auto",
            "dpi": 600,
            "copies": 1,
            "draw_borders": True
        })

        # Persist user choices into config.json
        try:
            persisted = global_settings.copy()
            persisted["printer"] = printer_name
            settings_manager.save_settings(persisted)
        except Exception:
            pass

        jobs_data = []
        for item in file_queue:
            settings = item.get("settings", {})
            if settings.get("use_custom"):
                job_spec = {
                    "path": item["path"],
                    "name": item["filename"],
                    "page_range": settings.get("page_range", "all"),
                    "nup": settings.get("nup", global_settings.get("nup", 1)),
                    "color": settings.get("color", global_settings.get("color", "bw")),
                    "orientation": settings.get("orientation", global_settings.get("orientation", "auto")),
                    "copies": settings.get("copies", global_settings.get("copies", 1)),
                    "draw_borders": global_settings.get("draw_borders", True)
                }
            else:
                job_spec = {
                    "path": item["path"],
                    "name": item["filename"],
                    "page_range": "all",
                    "nup": global_settings.get("nup", 1),
                    "color": global_settings.get("color", "bw"),
                    "orientation": global_settings.get("orientation", "auto"),
                    "copies": global_settings.get("copies", 1),
                    "draw_borders": global_settings.get("draw_borders", True)
                }
            jobs_data.append(job_spec)

        job_id = str(uuid.uuid4())
        print_jobs[job_id] = {
            "status": "pending",
            "progress": {"percent": 0, "message": "প্রিন্ট স্পুলার প্রস্তুত হচ্ছে..."},
            "error": None,
            "cancel_requested": False
        }

        thread = threading.Thread(
            target=run_print_worker,
            args=(job_id, printer_name, global_settings, jobs_data),
            daemon=True
        )
        thread.start()

        return jsonify({"success": True, "job_id": job_id})
    except Exception as e:
        traceback.print_exc()
        return jsonify({"success": False, "error": f"প্রিন্ট রিকোয়েস্ট শুরু করা যায়নি: {str(e)}"}), 500


@app.route("/api/print/status/<job_id>", methods=["GET"])
def get_print_status(job_id):
    """Check print job progress and status."""
    if job_id not in print_jobs:
        return jsonify({"success": False, "error": "Job not found"}), 404
    return jsonify({"success": True, "job": print_jobs[job_id]})


@app.route("/api/print/cancel/<job_id>", methods=["POST"])
def cancel_print(job_id):
    """Request print job cancellation."""
    if job_id in print_jobs:
        print_jobs[job_id]["cancel_requested"] = True
        return jsonify({"success": True})
    return jsonify({"success": False, "error": "Job not found"}), 404


def open_browser():
    """Wait for server to start, then open browser."""
    time.sleep(1.2)
    webbrowser.open("http://127.0.0.1:5055")


if __name__ == "__main__":
    # Clean previous uploads
    for f in os.listdir(UPLOAD_DIR):
        f_path = os.path.join(UPLOAD_DIR, f)
        if os.path.isfile(f_path):
            try:
                os.remove(f_path)
            except Exception:
                pass
    for f in os.listdir(THUMB_DIR):
        try:
            os.remove(os.path.join(THUMB_DIR, f))
        except Exception:
            pass

    threading.Thread(target=open_browser, daemon=True).start()
    print("==================================================")
    print("   Print Studio Server running at http://127.0.0.1:5055")
    print("   Press Ctrl+C to stop.")
    print("==================================================")
    app.run(host="127.0.0.1", port=5055, debug=False, threaded=True)
