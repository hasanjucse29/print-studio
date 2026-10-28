# 🖨️ Print Studio

> **Modern Batch PDF Printing Suite for Windows**  
> ড্র্যাগ-অ্যান্ড-ড্রপ ও ওয়ান-ক্লিক সাইলেন্ট পিডিএফ প্রিন্টিং টুল।

[![Platform](https://img.shields.io/badge/Platform-Windows-0078D6?logo=windows&logoColor=white)](#)
[![Python](https://img.shields.io/badge/Python-3.10%2B-3776AB?logo=python&logoColor=white)](#)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

Print Studio simplifies recurring PDF printing tasks. Instead of opening each document one by one in a PDF reader and manually choosing printer settings each time, you simply **drag and drop all your PDFs** into the local web interface and execute a single **"Print All"** command.

---

## ✨ Key Features (প্রধান সুবিধাসমূহ)

- 📂 **Drag & Drop Batch Queue:** Drop single or multiple PDF files directly. See page counts, file sizes, and instant thumbnail previews.
- ⚙️ **Smart Defaults Pre-selected:**
  - **Printer:** Auto-detects and prioritizes hardware printers (e.g., Toshiba, HP) with Windows default pre-selected.
  - **Color Mode:** Black & White (Grayscale) by default; full color optional.
  - **Paper Size:** A4 (210 × 297 mm) standard.
  - **Duplex:** Single-sided (1-sided) by default; Booklet (long-edge) or Calendar (short-edge) 2-sided available.
  - **N-Up Multi-Page Layout:** 1-up, 2-up (side-by-side), 4-up (2×2 grid), and 6-up with clean borders.
  - **Copies:** Multi-copy stepper.
- 💎 **Print Quality & Auto Max DPI:** Dynamically discovers connected printer hardware resolutions (`win32print.DeviceCapabilities`) and **automatically pre-selects the maximum hardware DPI (e.g. 600 DPI)** for razor-sharp vector text and barcodes.
- 🔄 **Automatic Orientation Matching:** Automatically detects whether each PDF page is Portrait or Landscape and instructs the printer driver accordingly—no upside-down or sideways pages.
- 🎛️ **Global + Per-File Customization:** Apply settings globally to all queued files at once, with an option to customize individual files (page ranges like `1-3, 5`, orientation, or color) on demand.
- 🚀 **Silent GDI Spooling:** Sends print jobs directly to Windows print spooler via Windows GDI (`pywin32`) without launching Adobe Acrobat, browser dialogs, or blocking popups.
- 🔒 **Thread-Safe Architecture:** Protected with `PDFIUM_LOCK` and disk thumbnail caching to guarantee stability even under heavy batch uploads.

---

## 🏗️ Architecture

```
Print Studio Architecture
│
├── Frontend (Browser UI: http://127.0.0.1:5055)
│   ├── Drag & drop dropzone
│   ├── Live PDF card queue & reordering
│   ├── Dynamic printer & quality DPI detection
│   └── Live composite sheet preview & progress modal
│
├── Backend (Flask Local Server: app.py)
│   ├── REST API endpoints (/api/printers, /api/upload, /api/print, etc.)
│   ├── Disk thumbnail caching
│   └── Background print worker thread
│
└── Engine (print_engine.py)
    ├── PDFium rasterization (PDFIUM_LOCK protected)
    ├── N-up sheet compositing & color conversion (Pillow)
    └── Direct Windows Spooler DC communication (pywin32 / DEVMODE)
```

---

## 🚀 Quick Start (কীভাবে চালাবেন)

### 1. Prerequisites (প্রয়োজনীয় বিষয়াদি)
- **Operating System:** Windows 10 or Windows 11
- **Python:** Python 3.10+ installed and added to system `PATH`

### 2. Installation (ইন্সটলেশন)
Clone the repository or download the folder:
```bash
git clone https://github.com/hasanjucse29/print-studio.git
cd print-studio
pip install -r requirements.txt
```

### 3. Launching & Installing (চালু ও ইনস্টল করার পদ্ধতি)

- **Option A (Official Windows Installer - Setup Wizard):**  
  Run **`dist/PrintStudio_Setup_v1.0.exe`**. Standard Windows installation wizard. Installs cleanly to AppData/Programs, creates Start Menu and Desktop shortcuts, and registers in Windows Settings (Add or Remove Programs).
- **Option B (Standalone Portable Window):**  
  Run **`dist/PrintStudio.exe`**. Launches in its own dedicated, native desktop window (no browser tabs, no terminal). Completely portable on any USB drive.
- **Option C (Standard Web Launcher):**  
  Double-click **`Run_Print_Studio.bat`**. This launches the local server and opens your default browser.
- **Option D (Quiet Background Launcher):**  
  Double-click **`Start_Print_Studio_Quiet.vbs`**. Runs silently in the background.

### 4. Compiling Executables & Installers (বিল্ড করার পদ্ধতি)
- **Rebuild Portable .exe:** Double-click **`build_portable.bat`** (output: `dist/PrintStudio.exe`).
- **Rebuild Windows Installer:** Double-click **`build_installer.bat`** (output: `dist/PrintStudio_Setup_v1.0.exe`).

---

## 📁 Repository Structure

```
print-studio/
├── app.py                     # Flask backend application & REST API
├── print_engine.py            # PDF rendering, N-up compositor & Windows GDI DC driver
├── requirements.txt           # Python package dependencies
├── .gitignore                 # Excludes temp uploads, cache, and personal files
├── LICENSE                    # MIT License
├── README.md                  # Documentation
├── Run_Print_Studio.bat       # 1-Click launcher batch script
├── Start_Print_Studio_Quiet.vbs # Silent background launcher
├── templates/
│   └── index.html             # Single-page web application UI
├── static/
│   ├── css/
│   │   └── style.css          # Sleek custom stylesheet & animations
│   └── js/
│       └── app.js             # Client application logic, queue & drag-drop
└── uploads/                   # Runtime temporary upload directory (ignored by git)
    ├── .gitkeep
    └── thumbs/
        └── .gitkeep
```

---

## 👨‍💻 Developer Information

**Md Hasan Al Mamun**  
- 🎓 **Department of Computer Science & Engineering**, Jahangirnagar University (JU CSE 29)
- ✉️ **Email:** [jucse29.399@gmail.com](mailto:jucse29.399@gmail.com)
- 🐙 **GitHub:** [@hasanjucse29](https://github.com/hasanjucse29)
- 📦 **Repository:** [https://github.com/hasanjucse29/print-studio](https://github.com/hasanjucse29/print-studio)

---

## 📄 License

Copyright © 2026 **Md Hasan Al Mamun**. Released under the [MIT License](LICENSE).
