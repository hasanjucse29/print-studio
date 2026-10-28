// ==========================================================================
// Print Studio - Frontend Application Logic
// ==========================================================================

document.addEventListener("DOMContentLoaded", () => {
    // State
    const state = {
        printers: [],
        selectedPrinter: "",
        queue: [],
        globalSettings: {
            color: "bw",
            paper: "A4",
            duplex: "simplex",
            nup: 1,
            orientation: "auto",
            dpi: 600,
            copies: 1,
            draw_borders: true
        },
        currentJobId: null,
        pollInterval: null
    };

    // DOM Elements
    const dropzone = document.getElementById("dropzone");
    const fileInput = document.getElementById("fileInput");
    const fileList = document.getElementById("fileList");
    const emptyState = document.getElementById("emptyState");
    const fileCountEl = document.getElementById("fileCount");
    const totalPageBadge = document.getElementById("totalPageBadge");
    const btnClearAll = document.getElementById("btnClearAll");
    const btnAddMore = document.getElementById("btnAddMore");
    const printerSelect = document.getElementById("printerSelect");
    const qualitySelect = document.getElementById("qualitySelect");
    const activePrinterBadge = document.getElementById("activePrinterBadge");
    const activePrinterName = document.getElementById("activePrinterName");
    const headerStatusDot = document.getElementById("headerStatusDot");
    const headerStatusTag = document.getElementById("headerStatusTag");
    const printerStatusPill = document.getElementById("printerStatusPill");
    const printerStatusText = document.getElementById("printerStatusText");
    const btnRefreshPrinters = document.getElementById("btnRefreshPrinters");
    const colorModeControl = document.getElementById("colorModeControl");
    const paperSizeSelect = document.getElementById("paperSizeSelect");
    const duplexSelect = document.getElementById("duplexSelect");
    const nupControl = document.getElementById("nupControl");
    const chkDrawBorders = document.getElementById("chkDrawBorders");
    const orientationSelect = document.getElementById("orientationSelect");
    const copiesInput = document.getElementById("copiesInput");
    const btnMinusCopy = document.getElementById("btnMinusCopy");
    const btnPlusCopy = document.getElementById("btnPlusCopy");
    const summaryPrinter = document.getElementById("summaryPrinter");
    const summaryPrinterStatus = document.getElementById("summaryPrinterStatus");
    const summaryColorPaper = document.getElementById("summaryColorPaper");
    const summaryQuality = document.getElementById("summaryQuality");
    const summarySheets = document.getElementById("summarySheets");
    const btnShowSheetPreview = document.getElementById("btnShowSheetPreview");
    const btnPrintAll = document.getElementById("btnPrintAll");

    // Modal elements
    const previewModal = document.getElementById("previewModal");
    const btnClosePreview = document.getElementById("btnClosePreview");
    const btnClosePreviewFooter = document.getElementById("btnClosePreviewFooter");
    const previewModalImg = document.getElementById("previewModalImg");
    const previewSpinner = document.getElementById("previewSpinner");
    const progressModal = document.getElementById("progressModal");
    const progressTitle = document.getElementById("progressTitle");
    const progressFilename = document.getElementById("progressFilename");
    const progressDetails = document.getElementById("progressDetails");
    const progressBarFill = document.getElementById("progressBarFill");
    const progressPercent = document.getElementById("progressPercent");
    const progressError = document.getElementById("progressError");
    const btnCancelPrint = document.getElementById("btnCancelPrint");
    const btnDonePrint = document.getElementById("btnDonePrint");
    const btnOpenAbout = document.getElementById("btnOpenAbout");
    const aboutModal = document.getElementById("aboutModal");
    const btnCloseAbout = document.getElementById("btnCloseAbout");
    const btnCloseAboutFooter = document.getElementById("btnCloseAboutFooter");
    const toastContainer = document.getElementById("toastContainer");

    // ================= Toast Helper =================
    function showToast(message, type = "info") {
        const toast = document.createElement("div");
        toast.className = `toast ${type === "error" ? "toast-error" : type === "success" ? "toast-success" : ""}`;
        
        let icon = "fa-circle-info";
        if (type === "success") icon = "fa-circle-check";
        if (type === "error") icon = "fa-circle-exclamation";
        
        toast.innerHTML = `<i class="fa-solid ${icon}"></i> <span>${message}</span>`;
        toastContainer.appendChild(toast);
        
        setTimeout(() => {
            toast.style.opacity = "0";
            toast.style.transform = "translateY(10px)";
            setTimeout(() => toast.remove(), 200);
        }, 3200);
    }

    // ================= Load Printers & Saved Settings =================
    async function loadSavedSettings() {
        try {
            const res = await fetch("/api/settings");
            const data = await res.json();
            if (data.success && data.settings) {
                const s = data.settings;
                Object.assign(state.globalSettings, s);
                
                // Update UI elements to match loaded settings
                if (s.color) {
                    colorModeControl.querySelectorAll(".segment-btn").forEach(btn => {
                        btn.classList.toggle("active", btn.dataset.color === s.color);
                    });
                }
                if (s.paper && paperSizeSelect) paperSizeSelect.value = s.paper;
                if (s.duplex && duplexSelect) duplexSelect.value = s.duplex;
                if (s.nup && nupControl) {
                    nupControl.querySelectorAll(".nup-btn").forEach(btn => {
                        btn.classList.toggle("active", parseInt(btn.dataset.nup) === s.nup);
                    });
                }
                if (s.orientation && orientationSelect) orientationSelect.value = s.orientation;
                if (s.copies && copiesInput) copiesInput.value = s.copies;
                if (s.draw_borders !== undefined && chkDrawBorders) chkDrawBorders.checked = s.draw_borders;
                
                return s;
            }
        } catch (e) {
            console.error("Could not load saved settings:", e);
        }
        return null;
    }

    let saveTimeout = null;
    function persistSettings() {
        clearTimeout(saveTimeout);
        saveTimeout = setTimeout(async () => {
            try {
                const payload = Object.assign({}, state.globalSettings, { printer: state.selectedPrinter });
                await fetch("/api/settings", {
                    method: "POST",
                    headers: { "Content-Type": "application/json" },
                    body: JSON.stringify(payload)
                });
            } catch (e) {
                console.error("Could not persist settings:", e);
            }
        }, 500);
    }

    async function loadPrinters() {
        try {
            printerSelect.innerHTML = '<option value="">প্রিন্টার খোঁজা হচ্ছে...</option>';
            const [printersRes, savedSettings] = await Promise.all([
                fetch("/api/printers").then(r => r.json()),
                loadSavedSettings()
            ]);
            
            if (printersRes.success && printersRes.printers.length > 0) {
                state.printers = printersRes.printers;
                printerSelect.innerHTML = "";
                
                let chosenPrinter = "";
                // If saved printer exists and is in the list, prefer it
                if (savedSettings && savedSettings.printer && state.printers.some(p => p.name === savedSettings.printer)) {
                    chosenPrinter = savedSettings.printer;
                } else {
                    // Prefer online physical printer first, then any physical, then default
                    const onlinePhysical = state.printers.find(p => !p.is_virtual && p.status && p.status.is_ready);
                    const anyPhysical = state.printers.find(p => !p.is_virtual);
                    const defaultPrinter = state.printers.find(p => p.is_default);
                    if (onlinePhysical) {
                        chosenPrinter = onlinePhysical.name;
                    } else if (anyPhysical) {
                        chosenPrinter = anyPhysical.name;
                    } else if (defaultPrinter) {
                        chosenPrinter = defaultPrinter.name;
                    } else {
                        chosenPrinter = state.printers[0].name;
                    }
                }

                state.printers.forEach(p => {
                    const opt = document.createElement("option");
                    opt.value = p.name;
                    
                    let statusIcon = "🟢";
                    let statusLabel = p.status ? p.status.short_label : "রেডি";
                    if (p.status?.state === "offline") {
                        statusIcon = "🔴";
                    } else if (p.status?.state === "busy" || p.status?.state === "printing") {
                        statusIcon = "🟡";
                    } else if (p.status?.state === "warning" || p.status?.state === "paper_out" || p.status?.state === "paper_jam") {
                        statusIcon = "⚠️";
                    } else if (p.is_virtual) {
                        statusIcon = "⚪";
                    }

                    const defTag = p.is_default ? " ★" : "";
                    opt.textContent = `${statusIcon} ${p.name} [${statusLabel}]${defTag}`;
                    
                    if (p.name === chosenPrinter) {
                        opt.selected = true;
                        state.selectedPrinter = p.name;
                    }
                    printerSelect.appendChild(opt);
                });

                state.selectedPrinter = chosenPrinter;
                updatePrinterStatusDisplay(chosenPrinter);
                updateQualityDropdown(chosenPrinter);
                updateSummary();
            } else {
                printerSelect.innerHTML = '<option value="">কোনো প্রিন্টার পাওয়া যায়নি</option>';
                showToast("কোনো প্রিন্টার পাওয়া যায়নি। উইন্ডোজ প্রিন্টার কানেক্ট আছে কিনা নিশ্চিত করুন।", "error");
            }
        } catch (err) {
            console.error("Error loading printers:", err);
            showToast("প্রিন্টার তালিকা লোড করা যায়নি", "error");
        }
    }

    function updatePrinterStatusDisplay(printerName, liveStatus = null) {
        const printer = state.printers.find(p => p.name === printerName);
        const status = liveStatus || (printer && printer.status) || {
            state: "unknown",
            label: "অজ্ঞাত",
            short_label: "অজ্ঞাত",
            css: "unknown",
            icon: "fa-circle-question",
            is_ready: false
        };

        if (printer && liveStatus) {
            printer.status = liveStatus;
        }

        const cssClass = `status-${status.css || 'unknown'}`;
        const allStatusClasses = ["status-ready", "status-offline", "status-busy", "status-warning", "status-unknown"];

        // 1. Top Header Badge
        if (activePrinterName) activePrinterName.textContent = printerName || "প্রিন্টার পাওয়া যায়নি";
        if (headerStatusDot) {
            headerStatusDot.className = "status-indicator-dot";
            headerStatusDot.classList.add(cssClass);
        }
        if (headerStatusTag) {
            headerStatusTag.className = "status-tag";
            headerStatusTag.classList.add(cssClass);
            headerStatusTag.textContent = status.short_label || "রেডি";
        }

        // 2. Settings Panel Status Pill
        if (printerStatusPill) {
            printerStatusPill.className = "printer-status-pill";
            printerStatusPill.classList.add(cssClass);
            const dot = printerStatusPill.querySelector(".status-dot");
            if (dot) {
                dot.className = "status-dot";
                dot.classList.add(cssClass);
            }
        }
        if (printerStatusText) {
            printerStatusText.textContent = status.label || "রেডি";
        }

        // 3. Summary Strip
        if (summaryPrinter) summaryPrinter.textContent = printerName || "-";
        if (summaryPrinterStatus) {
            summaryPrinterStatus.className = "summary-status-tag";
            summaryPrinterStatus.classList.add(cssClass);
            summaryPrinterStatus.textContent = status.short_label || "রেডি";
        }
    }

    function updateQualityDropdown(printerName) {
        if (!qualitySelect) return;
        qualitySelect.innerHTML = "";
        
        const printer = state.printers.find(p => p.name === printerName);
        let options = (printer && printer.quality_options && printer.quality_options.length > 0)
            ? printer.quality_options
            : null;

        // Fallback quality tiers if not provided by backend
        if (!options) {
            const dpis = (printer && printer.supported_dpis && printer.supported_dpis.length > 0)
                ? printer.supported_dpis
                : [300, 600];
            const maxDpi = (printer && printer.max_dpi) ? printer.max_dpi : Math.max(...dpis);
            const candidates = new Set(dpis);
            candidates.add(maxDpi);

            if (maxDpi >= 2400) [2400, 1200, 600, 300, 150].forEach(d => candidates.add(d));
            else if (maxDpi >= 1200) [1200, 600, 300, 150].forEach(d => candidates.add(d));
            else if (maxDpi >= 600) [600, 300, 150].forEach(d => candidates.add(d));
            else if (maxDpi >= 300) [300, 150].forEach(d => candidates.add(d));
            else [maxDpi, 150].forEach(d => candidates.add(d));

            const sorted = Array.from(candidates).filter(d => d <= maxDpi && d >= 100).sort((a, b) => b - a);
            options = sorted.map(d => {
                const isMax = (d === maxDpi);
                let lbl = `${d} DPI`;
                if (isMax) {
                    const desc = d >= 1200 ? "আল্ট্রা কোয়ালিটি" : d >= 600 ? "হাই কোয়ালিটি" : "স্ট্যান্ডার্ড";
                    lbl += ` (এই প্রিন্টারের সর্বোচ্চ / ${desc}) ★`;
                } else if (d >= 1200) {
                    lbl += " (আল্ট্রা কোয়ালিটি / ফাইন)";
                } else if (d >= 600) {
                    lbl += " (হাই কোয়ালিটি / স্পষ্ট)";
                } else if (d >= 300) {
                    lbl += " (স্ট্যান্ডার্ড কোয়ালিটি)";
                } else {
                    lbl += " (ড্রাফট / দ্রুত ও সাশ্রয়ী)";
                }
                return { dpi: d, label: lbl, is_default: isMax };
            });
        }

        // Dynamically pre-select THIS printer's maximum DPI by default!
        const maxOption = options.find(o => o.is_default) || options[0];
        const defaultDpi = maxOption ? maxOption.dpi : 600;
        const selectedDpi = defaultDpi;

        options.forEach(optData => {
            const opt = document.createElement("option");
            opt.value = optData.dpi;
            opt.textContent = optData.label;
            if (optData.dpi === selectedDpi) {
                opt.selected = true;
            }
            qualitySelect.appendChild(opt);
        });

        state.globalSettings.dpi = selectedDpi;
        if (summaryQuality) {
            summaryQuality.textContent = `${selectedDpi} DPI (সর্বোচ্চ)`;
        }
    }

    async function fetchLivePrinterStatus(printerName) {
        if (!printerName) return;
        try {
            const res = await fetch(`/api/printer_status?printer=${encodeURIComponent(printerName)}`);
            const data = await res.json();
            if (data.success && data.status) {
                updatePrinterStatusDisplay(printerName, data.status);
            }
        } catch (e) {
            // silent background poll failure
        }
    }

    // Dynamic background polling for live printer status (every 6 seconds)
    setInterval(() => {
        if (state.selectedPrinter && !state.currentJobId) {
            fetchLivePrinterStatus(state.selectedPrinter);
        }
    }, 6000);

    // Refresh live printer status whenever user returns to the window
    window.addEventListener("focus", () => {
        if (state.selectedPrinter && !state.currentJobId) {
            fetchLivePrinterStatus(state.selectedPrinter);
        }
    });

    // ================= Drag & Drop Setup =================
    ["dragenter", "dragover", "dragleave", "drop"].forEach(eventName => {
        document.body.addEventListener(eventName, e => e.preventDefault(), false);
        dropzone.addEventListener(eventName, e => e.preventDefault(), false);
    });

    ["dragenter", "dragover"].forEach(eventName => {
        dropzone.addEventListener(eventName, () => dropzone.classList.add("drag-active"), false);
    });

    ["dragleave", "drop"].forEach(eventName => {
        dropzone.addEventListener(eventName, () => dropzone.classList.remove("drag-active"), false);
    });

    dropzone.addEventListener("drop", e => {
        const dt = e.dataTransfer;
        const files = dt.files;
        if (files && files.length > 0) {
            handleFilesUpload(files);
        }
    });

    dropzone.addEventListener("click", () => fileInput.click());
    btnAddMore.addEventListener("click", () => fileInput.click());

    fileInput.addEventListener("change", e => {
        if (e.target.files && e.target.files.length > 0) {
            handleFilesUpload(e.target.files);
            fileInput.value = "";
        }
    });

    // ================= Upload Files =================
    async function handleFilesUpload(files) {
        const formData = new FormData();
        let pdfCount = 0;

        for (let i = 0; i < files.length; i++) {
            if (files[i].name.toLowerCase().endsWith(".pdf")) {
                formData.append("files", files[i]);
                pdfCount++;
            }
        }

        if (pdfCount === 0) {
            showToast("দয়া করে শুধুমাত্র পিডিএফ (.pdf) ফাইল সিলেক্ট করুন", "error");
            return;
        }

        showToast(`${pdfCount}টি ফাইল প্রসেস করা হচ্ছে...`);

        try {
            const res = await fetch("/api/upload", {
                method: "POST",
                body: formData
            });
            const data = await res.json();

            if (data.success) {
                showToast(`${data.added.length}টি পিডিএফ সফলভাবে যোগ করা হয়েছে!`, "success");
                await refreshQueue();
            } else {
                showToast(data.error || "আপলোডে সমস্যা হয়েছে", "error");
            }
        } catch (err) {
            console.error("Upload error:", err);
            showToast("ফাইল আপলোড ব্যর্থ হয়েছে", "error");
        }
    }

    // ================= Refresh & Render Queue =================
    async function refreshQueue() {
        try {
            const res = await fetch("/api/queue");
            const data = await res.json();
            if (data.success) {
                state.queue = data.queue;
                renderQueue();
                updateSummary();
            }
        } catch (err) {
            console.error("Queue fetch error:", err);
        }
    }

    function renderQueue() {
        fileList.innerHTML = "";
        const count = state.queue.length;
        fileCountEl.textContent = count;
        
        let totalPages = 0;
        state.queue.forEach(item => totalPages += item.page_count);
        totalPageBadge.textContent = `মোট পেজ: ${totalPages}`;

        if (count === 0) {
            emptyState.classList.remove("hidden");
            fileList.appendChild(emptyState);
            btnClearAll.disabled = true;
            btnPrintAll.disabled = true;
            btnShowSheetPreview.disabled = true;
            return;
        }

        emptyState.classList.add("hidden");
        btnClearAll.disabled = false;
        btnPrintAll.disabled = false;
        btnShowSheetPreview.disabled = false;

        state.queue.forEach((item, index) => {
            const card = document.createElement("div");
            card.className = "file-card";
            card.dataset.id = item.id;

            const isCustom = item.settings && item.settings.use_custom;

            card.innerHTML = `
                <div class="file-card-main">
                    <div class="file-thumb-container" onclick="window.previewSingleFile('${item.id}')" title="প্রিভিউ দেখতে ক্লিক করুন">
                        <img src="/api/thumbnail/${item.id}/0" alt="thumb" class="file-thumb-img" onerror="this.src='/static/icons/pdf-icon.png'">
                        <div class="file-thumb-overlay"><i class="fa-solid fa-magnifying-glass-plus"></i></div>
                    </div>
                    
                    <div class="file-details">
                        <div class="file-name" title="${item.filename}">${item.filename}</div>
                        <div class="file-meta">
                            <span class="meta-pill"><i class="fa-regular fa-file"></i> ${item.page_count} পেজ</span>
                            <span class="meta-pill"><i class="fa-solid fa-hard-drive"></i> ${item.size_str}</span>
                            ${item.is_mostly_portrait ? '<span class="meta-pill">পোর্ট্রেট</span>' : '<span class="meta-pill">ল্যান্ডস্কেপ</span>'}
                        </div>
                    </div>

                    <div class="file-actions-right">
                        ${index > 0 ? `<button class="btn-file-action" onclick="window.moveItem(${index}, -1)" title="উপরে নিন"><i class="fa-solid fa-arrow-up"></i></button>` : ''}
                        ${index < count - 1 ? `<button class="btn-file-action" onclick="window.moveItem(${index}, 1)" title="নিচে নিন"><i class="fa-solid fa-arrow-down"></i></button>` : ''}
                        <button class="btn-file-action btn-file-delete" onclick="window.deleteItem('${item.id}')" title="মুছে ফেলুন">
                            <i class="fa-regular fa-trash-can"></i>
                        </button>
                    </div>
                </div>

                <!-- Custom Per-File Settings Accordion -->
                <div class="custom-settings-toggle">
                    <label class="custom-checkbox">
                        <input type="checkbox" class="chk-custom-toggle" data-id="${item.id}" ${isCustom ? "checked" : ""}>
                        <span class="checkmark"></span>
                        এই ফাইলের জন্য কাস্টম সেটিংস দিন
                    </label>
                </div>

                <div class="custom-settings-panel ${isCustom ? "" : "hidden"}" id="customPanel_${item.id}">
                    <div class="custom-field">
                        <label>পেজ রেঞ্জ (Page Range)</label>
                        <input type="text" class="custom-input-range" data-id="${item.id}" value="${item.settings.page_range || 'all'}" placeholder="e.g. 1-3, 5 বা all">
                    </div>
                    <div class="custom-field">
                        <label>পেজ প্রতি শিট</label>
                        <select class="custom-select-nup" data-id="${item.id}">
                            <option value="1" ${item.settings.nup == 1 ? "selected" : ""}>১টি পেজ</option>
                            <option value="2" ${item.settings.nup == 2 ? "selected" : ""}>২টি পেজ</option>
                            <option value="4" ${item.settings.nup == 4 ? "selected" : ""}>৪টি পেজ</option>
                            <option value="6" ${item.settings.nup == 6 ? "selected" : ""}>৬টি পেজ</option>
                        </select>
                    </div>
                    <div class="custom-field">
                        <label>কালার</label>
                        <select class="custom-select-color" data-id="${item.id}">
                            <option value="bw" ${item.settings.color == "bw" ? "selected" : ""}>ব্ল্যাক অ্যান্ড হোয়াইট</option>
                            <option value="color" ${item.settings.color == "color" ? "selected" : ""}>কালার</option>
                        </select>
                    </div>
                    <div class="custom-field">
                        <label>ওরিয়েন্টেশন</label>
                        <select class="custom-select-orient" data-id="${item.id}">
                            <option value="auto" ${(!item.settings.orientation || item.settings.orientation === "auto") ? "selected" : ""}>অটো (ফাইলের মতো)</option>
                            <option value="portrait" ${item.settings.orientation === "portrait" ? "selected" : ""}>পোর্ট্রেট (লম্বা)</option>
                            <option value="landscape" ${item.settings.orientation === "landscape" ? "selected" : ""}>ল্যান্ডস্কেপ (আড়াআড়ি)</option>
                        </select>
                    </div>
                    <div class="custom-field">
                        <label>কপি</label>
                        <input type="number" min="1" max="99" class="custom-input-copies" data-id="${item.id}" value="${item.settings.copies || 1}">
                    </div>
                </div>
            `;

            fileList.appendChild(card);
        });

        attachCardEventListeners();
    }

    function attachCardEventListeners() {
        // Custom Settings Toggle Checkbox
        document.querySelectorAll(".chk-custom-toggle").forEach(chk => {
            chk.addEventListener("change", async e => {
                const fid = e.target.dataset.id;
                const panel = document.getElementById(`customPanel_${fid}`);
                const isCustom = e.target.checked;
                
                if (isCustom) panel.classList.remove("hidden");
                else panel.classList.add("hidden");

                await updateItemSettingsOnServer(fid, { use_custom: isCustom });
                updateSummary();
            });
        });

        // Custom Inputs
        document.querySelectorAll(".custom-input-range").forEach(inp => {
            inp.addEventListener("change", async e => {
                await updateItemSettingsOnServer(e.target.dataset.id, { page_range: e.target.value.trim() });
                updateSummary();
            });
        });

        document.querySelectorAll(".custom-select-nup").forEach(sel => {
            sel.addEventListener("change", async e => {
                await updateItemSettingsOnServer(e.target.dataset.id, { nup: parseInt(sel.value) });
                updateSummary();
            });
        });

        document.querySelectorAll(".custom-select-color").forEach(sel => {
            sel.addEventListener("change", async e => {
                await updateItemSettingsOnServer(e.target.dataset.id, { color: sel.value });
                updateSummary();
            });
        });

        document.querySelectorAll(".custom-select-orient").forEach(sel => {
            sel.addEventListener("change", async e => {
                await updateItemSettingsOnServer(e.target.dataset.id, { orientation: sel.value });
                updateSummary();
            });
        });

        document.querySelectorAll(".custom-input-copies").forEach(inp => {
            inp.addEventListener("change", async e => {
                const copies = Math.max(1, parseInt(inp.value) || 1);
                inp.value = copies;
                await updateItemSettingsOnServer(e.target.dataset.id, { copies: copies });
                updateSummary();
            });
        });
    }

    async function updateItemSettingsOnServer(fileId, patch) {
        try {
            await fetch(`/api/queue/update/${fileId}`, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(patch)
            });
            // Update in-memory state
            const target = state.queue.find(item => item.id === fileId);
            if (target) {
                target.settings = Object.assign(target.settings || {}, patch);
            }
        } catch (err) {
            console.error("Update item error:", err);
        }
    }

    // Global helper methods mounted to window for card clicks
    window.deleteItem = async function(fileId) {
        try {
            const res = await fetch(`/api/queue/${fileId}`, { method: "DELETE" });
            const data = await res.json();
            if (data.success) {
                showToast("ফাইল তালিকা থেকে সরানো হয়েছে");
                await refreshQueue();
            }
        } catch (err) {
            console.error("Delete error:", err);
        }
    };

    window.moveItem = async function(currIdx, direction) {
        const newIdx = currIdx + direction;
        if (newIdx < 0 || newIdx >= state.queue.length) return;

        const temp = state.queue[currIdx];
        state.queue[currIdx] = state.queue[newIdx];
        state.queue[newIdx] = temp;

        const ids = state.queue.map(item => item.id);
        try {
            await fetch("/api/queue/reorder", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ ids: ids })
            });
            renderQueue();
            updateSummary();
        } catch (err) {
            console.error("Reorder error:", err);
        }
    };

    window.previewSingleFile = function(fileId) {
        openSheetPreview(fileId);
    };

    // ================= Clear All =================
    btnClearAll.addEventListener("click", async () => {
        if (!confirm("আপনি কি নিশ্চিত যে কিউ থেকে সব ফাইল মুছে ফেলতে চান?")) return;
        try {
            const res = await fetch("/api/queue/clear", { method: "POST" });
            const data = await res.json();
            if (data.success) {
                showToast("সব ফাইল মুছে ফেলা হয়েছে");
                await refreshQueue();
            }
        } catch (err) {
            console.error("Clear error:", err);
        }
    });

    // ================= Settings Event Handlers =================
    printerSelect.addEventListener("change", e => {
        state.selectedPrinter = e.target.value;
        updatePrinterStatusDisplay(e.target.value);
        updateQualityDropdown(e.target.value);
        updateSummary();
        fetchLivePrinterStatus(e.target.value);
    });

    if (qualitySelect) {
        qualitySelect.addEventListener("change", e => {
            state.globalSettings.dpi = parseInt(e.target.value);
            updateSummary();
        });
    }

    btnRefreshPrinters.addEventListener("click", async () => {
        btnRefreshPrinters.classList.add("fa-spin");
        await loadPrinters();
        setTimeout(() => btnRefreshPrinters.classList.remove("fa-spin"), 600);
        showToast("প্রিন্টার তালিকা রিফ্রেশ করা হয়েছে", "success");
    });

    // Color Segmented Control
    colorModeControl.querySelectorAll(".segment-btn").forEach(btn => {
        btn.addEventListener("click", () => {
            colorModeControl.querySelectorAll(".segment-btn").forEach(b => b.classList.remove("active"));
            btn.classList.add("active");
            state.globalSettings.color = btn.dataset.color;
            updateSummary();
        });
    });

    // Paper Size
    paperSizeSelect.addEventListener("change", e => {
        state.globalSettings.paper = e.target.value;
        updateSummary();
    });

    // Duplex
    duplexSelect.addEventListener("change", e => {
        state.globalSettings.duplex = e.target.value;
        updateSummary();
    });

    // N-up Pill Buttons
    nupControl.querySelectorAll(".nup-btn").forEach(btn => {
        btn.addEventListener("click", () => {
            nupControl.querySelectorAll(".nup-btn").forEach(b => b.classList.remove("active"));
            btn.classList.add("active");
            state.globalSettings.nup = parseInt(btn.dataset.nup);
            updateSummary();
        });
    });

    // Border Checkbox
    chkDrawBorders.addEventListener("change", e => {
        state.globalSettings.draw_borders = e.target.checked;
    });

    // Orientation
    orientationSelect.addEventListener("change", e => {
        state.globalSettings.orientation = e.target.value;
    });

    // Copies Stepper
    btnMinusCopy.addEventListener("click", () => {
        const val = Math.max(1, parseInt(copiesInput.value) - 1);
        copiesInput.value = val;
        state.globalSettings.copies = val;
        updateSummary();
    });

    btnPlusCopy.addEventListener("click", () => {
        const val = Math.min(99, parseInt(copiesInput.value) + 1);
        copiesInput.value = val;
        state.globalSettings.copies = val;
        updateSummary();
    });

    copiesInput.addEventListener("change", () => {
        const val = Math.max(1, Math.min(99, parseInt(copiesInput.value) || 1));
        copiesInput.value = val;
        state.globalSettings.copies = val;
        updateSummary();
    });

    // ================= Summary Calculation =================
    function updateSummary() {
        const colorLabel = state.globalSettings.color === "bw" ? "B&W" : "কালার";
        summaryColorPaper.textContent = `${colorLabel} • ${state.globalSettings.paper}`;
        
        if (summaryQuality) {
            const currentPrinter = state.printers.find(p => p.name === state.selectedPrinter);
            const isMax = currentPrinter && currentPrinter.max_dpi === state.globalSettings.dpi;
            summaryQuality.textContent = `${state.globalSettings.dpi} DPI ${isMax ? "(ম্যাক্স)" : ""}`;
        }
        
        let totalSheets = 0;
        state.queue.forEach(item => {
            const isCustom = item.settings && item.settings.use_custom;
            const nup = isCustom ? parseInt(item.settings.nup || state.globalSettings.nup) : state.globalSettings.nup;
            const copies = isCustom ? parseInt(item.settings.copies || state.globalSettings.copies) : state.globalSettings.copies;
            
            // Approximation: if range is 'all' or empty, use page_count
            let pCount = item.page_count;
            if (isCustom && item.settings.page_range && item.settings.page_range !== "all") {
                // If specific range, estimate roughly
                const parts = item.settings.page_range.split(",");
                let est = 0;
                parts.forEach(p => {
                    if (p.includes("-")) {
                        const s = p.split("-");
                        est += Math.max(1, (parseInt(s[1]) || item.page_count) - (parseInt(s[0]) || 1) + 1);
                    } else if (p.trim()) {
                        est += 1;
                    }
                });
                pCount = Math.min(item.page_count, est || item.page_count);
            }

            const sheets = Math.ceil(pCount / nup) * copies;
            totalSheets += sheets;
        });

        // Adjust for duplex
        let physicalSheets = totalSheets;
        if (state.globalSettings.duplex !== "simplex") {
            physicalSheets = Math.ceil(totalSheets / 2);
        }

        if (state.queue.length === 0) {
            summarySheets.textContent = "0 টি কাগজ";
        } else {
            const duplexNote = state.globalSettings.duplex !== "simplex" ? " (ডুপ্লেক্স)" : "";
            summarySheets.textContent = `${physicalSheets} টি কাগজ${duplexNote}`;
        }
        
        persistSettings();
    }

    // ================= Sheet Preview Modal =================
    btnShowSheetPreview.addEventListener("click", () => {
        if (state.queue.length > 0) {
            openSheetPreview(state.queue[0].id);
        }
    });

    async function openSheetPreview(fileId) {
        previewModal.classList.add("open");
        previewSpinner.classList.remove("hidden");
        previewModalImg.classList.add("hidden");

        const target = state.queue.find(item => item.id === fileId) || state.queue[0];
        const nup = (target.settings && target.settings.use_custom) ? target.settings.nup : state.globalSettings.nup;
        const color = (target.settings && target.settings.use_custom) ? target.settings.color : state.globalSettings.color;
        const orient = state.globalSettings.orientation;

        try {
            const res = await fetch("/api/preview_sheet", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({
                    file_id: target.id,
                    nup: nup,
                    color: color,
                    orientation: orient
                })
            });

            if (res.ok) {
                const blob = await res.blob();
                const url = URL.createObjectURL(blob);
                previewModalImg.src = url;
                previewSpinner.classList.add("hidden");
                previewModalImg.classList.remove("hidden");
            } else {
                showToast("প্রিভিউ তৈরি করা সম্ভব হয়নি", "error");
                closePreviewModal();
            }
        } catch (err) {
            console.error("Preview error:", err);
            closePreviewModal();
        }
    }

    function closePreviewModal() {
        previewModal.classList.remove("open");
        previewModalImg.src = "";
    }

    btnClosePreview.addEventListener("click", closePreviewModal);
    btnClosePreviewFooter.addEventListener("click", closePreviewModal);

    // ================= About Modal =================
    function openAboutModal() {
        if (aboutModal) aboutModal.classList.add("open");
    }

    function closeAboutModal() {
        if (aboutModal) aboutModal.classList.remove("open");
    }

    if (btnOpenAbout) btnOpenAbout.addEventListener("click", openAboutModal);
    if (btnCloseAbout) btnCloseAbout.addEventListener("click", closeAboutModal);
    if (btnCloseAboutFooter) btnCloseAboutFooter.addEventListener("click", closeAboutModal);
    if (aboutModal) {
        aboutModal.addEventListener("click", e => {
            if (e.target === aboutModal) closeAboutModal();
        });
    }

    // External link handler to open links safely in default web browser
    document.querySelectorAll(".external-open-link").forEach(link => {
        link.addEventListener("click", async e => {
            e.preventDefault();
            const url = link.dataset.url || link.href;
            if (url) {
                try {
                    await fetch("/api/open_url", {
                        method: "POST",
                        headers: { "Content-Type": "application/json" },
                        body: JSON.stringify({ url })
                    });
                } catch (err) {
                    window.open(url, "_blank");
                }
            }
        });
    });

    // ================= One-Click Print Execution =================
    btnPrintAll.addEventListener("click", async () => {
        if (!state.selectedPrinter) {
            showToast("দয়া করে একটি প্রিন্টার নির্বাচন করুন", "error");
            return;
        }

        if (state.queue.length === 0) {
            showToast("প্রিন্ট করার জন্য কোনো ফাইল নেই", "error");
            return;
        }

        // Check if selected printer is offline and notify user
        const currentPrinter = state.printers.find(p => p.name === state.selectedPrinter);
        if (currentPrinter && currentPrinter.status && currentPrinter.status.state === "offline") {
            showToast("সতর্কতা: প্রিন্টারটি বর্তমানে অফলাইনে আছে! উইন্ডোজ কিউতে প্রিন্ট জমা থাকবে।", "info");
        }

        // Open progress modal
        progressModal.classList.add("open");
        progressTitle.innerHTML = '<i class="fa-solid fa-print"></i> প্রিন্ট স্পুলারে পাঠানো হচ্ছে...';
        progressFilename.textContent = "ডকুমেন্ট প্রস্তুত হচ্ছে...";
        progressDetails.textContent = "অনুগ্রহ করে অপেক্ষা করুন...";
        progressBarFill.style.width = "0%";
        progressPercent.textContent = "0%";
        progressError.classList.add("hidden");
        btnCancelPrint.classList.remove("hidden");
        btnDonePrint.classList.add("hidden");

        try {
            const res = await fetch("/api/print", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({
                    printer: state.selectedPrinter,
                    global_settings: state.globalSettings
                })
            });

            const data = await res.json();
            if (data.success) {
                state.currentJobId = data.job_id;
                startProgressPolling(data.job_id);
            } else {
                showProgressError(data.error || "প্রিন্ট শুরু করা যায়নি");
            }
        } catch (err) {
            console.error("Print request error:", err);
            showProgressError("সার্ভারের সাথে সংযোগ বিচ্ছিন্ন হয়েছে");
        }
    });

    function startProgressPolling(jobId) {
        if (state.pollInterval) clearInterval(state.pollInterval);

        state.pollInterval = setInterval(async () => {
            try {
                const res = await fetch(`/api/print/status/${jobId}`);
                const data = await res.json();

                if (!data.success) {
                    clearInterval(state.pollInterval);
                    showProgressError(data.error || "জব ট্র্যাকিং ব্যর্থ হয়েছে");
                    return;
                }

                const job = data.job;
                if (job.status === "running") {
                    const p = job.progress || {};
                    const pct = p.percent || 0;
                    progressBarFill.style.width = `${pct}%`;
                    progressPercent.textContent = `${pct}%`;
                    if (p.filename) {
                        progressFilename.textContent = `ডকুমেন্ট: ${p.filename}`;
                        progressDetails.textContent = `ফাইল ${p.doc_index}/${p.total_docs} • শিট ${p.overall_sheet}/${p.overall_total_sheets}`;
                    }
                } else if (job.status === "completed") {
                    clearInterval(state.pollInterval);
                    progressBarFill.style.width = "100%";
                    progressPercent.textContent = "100%";
                    progressTitle.innerHTML = '<i class="fa-solid fa-circle-check text-success"></i> প্রিন্ট সম্পন্ন হয়েছে!';
                    progressFilename.textContent = "সবগুলো ডকুমেন্ট সফলভাবে প্রিন্টারে পাঠানো হয়েছে।";
                    progressDetails.textContent = `মোট ${job.result ? job.result.sheets_printed : ''} টি শিট স্পুলারে প্রেরণ করা হয়েছে।`;
                    btnCancelPrint.classList.add("hidden");
                    btnDonePrint.classList.remove("hidden");
                    showToast("প্রিন্ট সফলভাবে সম্পন্ন হয়েছে!", "success");
                } else if (job.status === "cancelled") {
                    clearInterval(state.pollInterval);
                    progressTitle.textContent = "প্রিন্ট বাতিল করা হয়েছে";
                    progressFilename.textContent = "ব্যবহারকারী প্রিন্ট রিকোয়েস্ট বাতিল করেছেন।";
                    btnCancelPrint.classList.add("hidden");
                    btnDonePrint.classList.remove("hidden");
                } else if (job.status === "error") {
                    clearInterval(state.pollInterval);
                    showProgressError(job.error || "প্রিন্ট করার সময় ত্রুটি ঘটেছে");
                }
            } catch (err) {
                console.error("Polling error:", err);
            }
        }, 600);
    }

    function showProgressError(msg) {
        progressError.textContent = msg;
        progressError.classList.remove("hidden");
        progressTitle.innerHTML = '<i class="fa-solid fa-triangle-exclamation text-danger"></i> প্রিন্টে ত্রুটি!';
        btnCancelPrint.classList.add("hidden");
        btnDonePrint.classList.remove("hidden");
    }

    btnCancelPrint.addEventListener("click", async () => {
        if (state.currentJobId) {
            btnCancelPrint.disabled = true;
            btnCancelPrint.textContent = "বাতিল হচ্ছে...";
            try {
                await fetch(`/api/print/cancel/${state.currentJobId}`, { method: "POST" });
            } catch (err) {
                console.error("Cancel error:", err);
            }
        }
    });

    btnDonePrint.addEventListener("click", () => {
        progressModal.classList.remove("open");
        btnCancelPrint.disabled = false;
        btnCancelPrint.textContent = "বাতিল করুন";
    });

    // ================= Native Desktop UX Enhancements =================
    // Disable web context menu (Inspect, Reload, etc.)
    window.addEventListener("contextmenu", e => e.preventDefault());

    // Native Desktop Keyboard Shortcuts
    window.addEventListener("keydown", e => {
        // Disable F5 & Ctrl+R browser reloads
        if (e.key === "F5" || (e.ctrlKey && (e.key === "r" || e.key === "R"))) {
            e.preventDefault();
        }
        // Ctrl+O: Open File Chooser
        if (e.ctrlKey && (e.key === "o" || e.key === "O")) {
            e.preventDefault();
            fileInput.click();
        }
        // Ctrl+P: Quick Print All
        if (e.ctrlKey && (e.key === "p" || e.key === "P")) {
            e.preventDefault();
            if (btnPrintAll && !btnPrintAll.disabled) {
                btnPrintAll.click();
            }
        }
        // Escape: Close modals
        if (e.key === "Escape") {
            closePreviewModal();
            closeAboutModal();
            if (!progressModal.classList.contains("open") || !btnDonePrint.classList.contains("hidden")) {
                progressModal.classList.remove("open");
            }
        }
    });

    // ================= Initialize =================
    loadPrinters();
    refreshQueue();
});
