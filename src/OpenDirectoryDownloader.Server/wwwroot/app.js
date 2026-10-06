"use strict";

// Each category's extensions double as the per-file icon lookup (getFileIcon, checked in order - first
// match wins) and the Statistics panel's media-type pie chart (renderStats) - one definition, so adding an
// extension to one can't silently leave it missing from the other.
const FILE_CATEGORIES = [
	{ name: "Images", icon: "\u{1F5BC}\u{FE0F}", extensions: new Set(["jpg", "jpeg", "png", "gif", "webp", "bmp", "svg", "avif"]) },
	{ name: "Video", icon: "\u{1F3AC}", extensions: new Set(["mp4", "mkv", "webm", "avi", "mov", "wmv", "flv", "m4v", "mpg", "mpeg", "3gp"]) },
	{ name: "Audio", icon: "\u{1F3B5}", extensions: new Set(["mp3", "wav", "flac", "ogg", "m4a", "aac", "wma", "opus"]) },
	{ name: "Archives", icon: "\u{1F5DC}\u{FE0F}", extensions: new Set(["zip", "rar", "7z", "tar", "gz", "bz2", "xz", "tgz"]) },
	{ name: "PDFs", icon: "\u{1F4D5}", extensions: new Set(["pdf"]) },
	{ name: "Spreadsheets", icon: "\u{1F4CA}", extensions: new Set(["xls", "xlsx", "csv", "ods"]) },
	{ name: "Presentations", icon: "\u{1F4FD}\u{FE0F}", extensions: new Set(["ppt", "pptx", "odp", "key"]) },
	{ name: "Documents", icon: "\u{1F4DD}", extensions: new Set(["doc", "docx", "odt", "rtf", "txt", "md"]) },
	{ name: "Disk images", icon: "\u{1F4BE}", extensions: new Set(["iso", "img", "dmg"]) },
	{ name: "Executables", icon: "\u{2699}\u{FE0F}", extensions: new Set(["exe", "msi", "apk", "bat", "sh", "cmd"]) },
	{ name: "Code", icon: "\u{1F4BB}", extensions: new Set(["js", "ts", "jsx", "tsx", "py", "java", "c", "cpp", "h", "cs", "go", "rb", "php", "html", "htm", "css", "json", "xml", "yml", "yaml", "sql"]) },
];

const IMAGE_EXTENSIONS = FILE_CATEGORIES[0].extensions;

// A fixed, stable color per category (spread evenly around the hue wheel) so a slice always means the same
// thing between opens of the Statistics panel, regardless of how large it is that time. "Other" (extensions
// matching no category) sits outside the rotation as a neutral grey, since it's a leftover bucket, not a
// media type of its own.
const CATEGORY_COLORS = new Map(FILE_CATEGORIES.map((category, index) => [category.name, `hsl(${Math.round((index * 360) / FILE_CATEGORIES.length)}, 65%, 55%)`]));
CATEGORY_COLORS.set("Other", "#9099a6");

function findFileCategory(extension) {
	return FILE_CATEGORIES.find((category) => category.extensions.has(extension)) || null;
}

// Google Drive native files (Docs/Sheets/Slides/...) have no file extension at all - WebFile.Description
// carries a short type label for these (see GoogleDriveIndexMapping.GetFriendlyMimeTypeName server-side).
// Used as a whitelist too: a Description that ISN'T one of these keys belongs to some other parser (several
// generic listing parsers stash arbitrary text like a modified-date there) and must not be shown as a badge.
const DRIVE_TYPE_ICONS = {
	Doc: "\u{1F4DD}",
	Sheet: "\u{1F4CA}",
	Slide: "\u{1F4FD}\u{FE0F}",
	Drawing: "\u{1F3A8}",
	Form: "\u{1F4CB}",
	Script: "\u{1F4DC}",
	Site: "\u{1F310}",
	Jamboard: "\u{1F5C2}\u{FE0F}",
	"My Map": "\u{1F5FA}\u{FE0F}",
	Shortcut: "\u{1F517}",
	"Fusion Table": "\u{1F4CA}",
};

function getDriveTypeIcon(entry) {
	return (entry.description && DRIVE_TYPE_ICONS[entry.description]) || null;
}

function getFileIcon(entry) {
	const driveIcon = getDriveTypeIcon(entry);
	if (driveIcon) {
		return driveIcon;
	}

	const dot = entry.name.lastIndexOf(".");
	if (dot !== -1) {
		const category = findFileCategory(entry.name.slice(dot + 1).toLowerCase());
		if (category) {
			return category.icon;
		}
	}

	return "\u{1F4C4}";
}

const state = {
	rootUrl: null,
	currentUrl: null,
	currentParentUrl: null,
	currentEntries: [],
	filterTerm: "",
	sortField: "name",
	sortDirection: "asc",
	totalFiles: 0,
	totalDirectories: 0,
	totalSize: 0,
	databaseSizeBytes: 0,
};

let globalSearchTimer = null;
let lastGlobalResult = null;

const el = {
	dropOverlay: document.getElementById("drop-overlay"),
	breadcrumbs: document.getElementById("breadcrumbs"),
	upBtn: document.getElementById("up-btn"),
	sessionInfo: document.getElementById("session-info"),
	toolbar: document.getElementById("toolbar"),
	searchInput: document.getElementById("search-input"),
	searchClearBtn: document.getElementById("search-clear-btn"),
	galleryToggle: document.getElementById("gallery-toggle"),
	statsBtn: document.getElementById("stats-btn"),
	statsModal: document.getElementById("stats-modal"),
	statsCloseBtn: document.getElementById("stats-close-btn"),
	statsBody: document.getElementById("stats-body"),
	zipCurrentBtn: document.getElementById("zip-current-btn"),
	initialLoading: document.getElementById("initial-loading"),
	emptyState: document.getElementById("empty-state"),
	uploadStatus: document.getElementById("upload-status"),
	navProgress: document.getElementById("nav-progress"),
	browser: document.getElementById("browser"),
	summary: document.getElementById("summary"),
	gallery: document.getElementById("gallery"),
	entryList: document.getElementById("entry-list"),
	globalResults: document.getElementById("global-results"),
	globalResultsList: document.getElementById("global-results-list"),
	globalResultsNote: document.getElementById("global-results-note"),
	progressPanel: document.getElementById("progress-panel"),
	progressTitle: document.getElementById("progress-title"),
	progressBarFill: document.getElementById("progress-bar-fill"),
	progressDetail: document.getElementById("progress-detail"),
	toast: document.getElementById("toast"),
};

init();

async function init() {
	setupDragAndDrop();
	setupToolbar();
	setupSearch();
	setupHistory();
	setupStats();

	const status = await fetchJson("/api/status");
	renderSessionInfo(status);

	if (status.loaded && status.rootUrl) {
		state.rootUrl = status.rootUrl;

		const requestedTerm = getQueryFromLocation();
		applySearchTerm(requestedTerm);

		const requestedUrl = getDirFromLocation();
		const loaded = await loadDirectory(requestedUrl || status.rootUrl, { replace: true });

		if (!loaded && requestedUrl) {
			await loadDirectory(status.rootUrl, { replace: true });
		}

		scheduleGlobalSearch();
	} else {
		showEmptyState();
	}

	// Hidden only now (not from the start) - so whichever of the two views above just became visible
	// replaces it in the same tick, instead of a blank gap or a flash of "no database loaded" while the
	// initial /api/status request (and, if one's already loaded, the first directory fetch) is in flight.
	el.initialLoading.hidden = true;
}

function setupHistory() {
	window.addEventListener("popstate", (event) => {
		const url = event.state?.url || getDirFromLocation() || state.rootUrl;

		applySearchTerm(getQueryFromLocation());

		if (url) {
			loadDirectory(url, { pushState: false });
		}

		scheduleGlobalSearch();
	});
}

function getDirFromLocation() {
	return new URLSearchParams(location.search).get("dir");
}

function getQueryFromLocation() {
	return new URLSearchParams(location.search).get("q") || "";
}

function applySearchTerm(term) {
	state.filterTerm = term;
	el.searchInput.value = term;
	el.searchClearBtn.hidden = term.length === 0;
}

function buildLocation(url, term) {
	const params = new URLSearchParams();

	if (url && url !== state.rootUrl) {
		params.set("dir", url);
	}

	if (term) {
		params.set("q", term);
	}

	const query = params.toString();
	return query ? `/?${query}` : "/";
}

// Ctrl/Cmd/Shift+click, middle-click, or right-click "open in new tab" should all behave like a normal
// link (using its real href) instead of our SPA-style in-place navigation - only a plain left-click
// should be intercepted.
function isPlainLeftClick(e) {
	return e.button === 0 && !e.ctrlKey && !e.metaKey && !e.shiftKey && !e.altKey;
}

function handleNavigationClick(e, url) {
	e.stopPropagation();

	if (!isPlainLeftClick(e)) {
		return;
	}

	e.preventDefault();
	loadDirectory(url);
}

function setupToolbar() {
	el.galleryToggle.addEventListener("change", () => {
		renderEntries();
	});

	el.zipCurrentBtn.addEventListener("click", () => {
		if (state.currentUrl) {
			downloadZip(state.currentUrl);
		}
	});

	el.upBtn.addEventListener("click", () => {
		if (state.currentParentUrl) {
			loadDirectory(state.currentParentUrl);
		}
	});
}

function setupSearch() {
	el.searchInput.addEventListener("input", () => {
		state.filterTerm = el.searchInput.value;
		el.searchClearBtn.hidden = state.filterTerm.length === 0;
		renderEntries();
		updateSearchLocation();
		scheduleGlobalSearch();
	});

	el.searchClearBtn.addEventListener("click", () => {
		applySearchTerm("");
		renderEntries();
		updateSearchLocation();
		clearGlobalResults();
	});
}

function updateSearchLocation() {
	history.replaceState({ url: state.currentUrl }, "", buildLocation(state.currentUrl, state.filterTerm.trim()));
}

function scheduleGlobalSearch() {
	clearTimeout(globalSearchTimer);

	const term = state.filterTerm.trim();

	if (term.length < 2) {
		clearGlobalResults();
		return;
	}

	globalSearchTimer = setTimeout(() => runGlobalSearch(term), 300);
}

async function runGlobalSearch(term) {
	let result;
	try {
		result = await fetchJson(`/api/search?q=${encodeURIComponent(term)}`);
	} catch {
		return;
	}

	if (term !== state.filterTerm.trim()) {
		return;
	}

	lastGlobalResult = result;
	renderGlobalResults(result);
}

function clearGlobalResults() {
	lastGlobalResult = null;
	el.globalResults.hidden = true;
	el.globalResultsList.innerHTML = "";
	el.globalResultsNote.hidden = true;
}

function setupDragAndDrop() {
	let dragDepth = 0;

	window.addEventListener("dragenter", (e) => {
		if (!hasFiles(e)) {
			return;
		}
		e.preventDefault();
		dragDepth++;
		el.dropOverlay.hidden = false;
	});

	window.addEventListener("dragover", (e) => {
		if (!hasFiles(e)) {
			return;
		}
		e.preventDefault();
	});

	window.addEventListener("dragleave", (e) => {
		if (!hasFiles(e)) {
			return;
		}
		dragDepth = Math.max(0, dragDepth - 1);
		if (dragDepth === 0) {
			el.dropOverlay.hidden = true;
		}
	});

	window.addEventListener("drop", (e) => {
		if (!hasFiles(e)) {
			return;
		}
		e.preventDefault();
		dragDepth = 0;
		el.dropOverlay.hidden = true;

		const file = e.dataTransfer.files[0];
		if (!file) {
			return;
		}

		if (!file.name.toLowerCase().endsWith(".sqlite")) {
			showToast(`'${file.name}' doesn't look like a .sqlite database.`);
			return;
		}

		uploadDatabase(file);
	});

	function hasFiles(e) {
		return e.dataTransfer && Array.from(e.dataTransfer.types || []).includes("Files");
	}
}

function uploadDatabase(file) {
	const xhr = new XMLHttpRequest();

	xhr.open("POST", `/api/database?fileName=${encodeURIComponent(file.name)}`);

	xhr.upload.addEventListener("progress", (e) => {
		if (e.lengthComputable) {
			const percent = Math.round((e.loaded / e.total) * 100);
			el.uploadStatus.textContent = `Uploading '${file.name}'... ${percent}% (${formatBytes(e.loaded)} / ${formatBytes(e.total)})`;
		} else {
			el.uploadStatus.textContent = `Uploading '${file.name}'...`;
		}
	});

	xhr.addEventListener("load", async () => {
		if (xhr.status >= 200 && xhr.status < 300) {
			el.uploadStatus.textContent = "";
			const status = await fetchJson("/api/status");
			renderSessionInfo(status);
			if (status.loaded && status.rootUrl) {
				state.rootUrl = status.rootUrl;
				await loadDirectory(status.rootUrl, { replace: true });
			}
		} else {
			el.uploadStatus.textContent = "";
			showToast(describeUploadError(xhr));
		}
	});

	xhr.addEventListener("error", () => {
		el.uploadStatus.textContent = "";
		showToast("Upload failed.");
	});

	xhr.send(file);
}

function describeUploadError(xhr) {
	try {
		const body = JSON.parse(xhr.responseText);
		return body.error || "Could not load that database.";
	} catch {
		return "Could not load that database.";
	}
}

function showEmptyState() {
	el.emptyState.hidden = false;
	el.browser.hidden = true;
	el.toolbar.hidden = true;
	el.breadcrumbs.innerHTML = "";
}

function renderSessionInfo(status) {
	if (!status.loaded) {
		el.sessionInfo.hidden = true;
		return;
	}

	// Scan-wide totals (every file/directory, not just the current folder) - shown immediately once the
	// (cheap) status request resolves, ahead of whichever folder's own listing happens to load first.
	state.totalFiles = status.totalFiles || 0;
	state.totalDirectories = status.totalDirectories || 0;
	state.totalSize = status.totalSize || 0;
	state.databaseSizeBytes = status.databaseSizeBytes || 0;

	const parts = [`${formatCount(state.totalFiles)} file${state.totalFiles === 1 ? "" : "s"}`];
	parts.push(`${formatBytes(state.totalSize)} total`);
	parts.push(`Database: ${formatBytes(state.databaseSizeBytes)}`);
	parts.push(`First scanned ${formatDateTime(status.firstStartedAtUtc)}`);
	parts.push(status.completedAtUtc ? `Completed ${formatDateTime(status.completedAtUtc)}` : "Not completed");
	parts.push(`${status.runCount} run${status.runCount === 1 ? "" : "s"}`);

	if (status.parserTypes && status.parserTypes.length > 0) {
		const label = status.parserTypes.length === 1 ? "Type" : "Types";
		parts.push(`${label}: ${status.parserTypes.join(", ")}`);
	}

	el.sessionInfo.textContent = parts.join(" · ");
	el.sessionInfo.hidden = false;
}

function formatDateTime(iso) {
	const date = new Date(iso);
	const pad = (value) => String(value).padStart(2, "0");

	return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())} ${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

function setupStats() {
	el.statsBtn.addEventListener("click", openStats);
	el.statsCloseBtn.addEventListener("click", closeStats);

	el.statsModal.addEventListener("click", (e) => {
		if (e.target === el.statsModal) {
			closeStats();
		}
	});

	window.addEventListener("keydown", (e) => {
		if (e.key === "Escape" && !el.statsModal.hidden) {
			closeStats();
		}
	});
}

function closeStats() {
	el.statsModal.hidden = true;
}

// Unlike the lightweight totals already shown in the session-info bar (see renderSessionInfo, sourced from
// /api/status), the per-extension breakdown here is a full table scan server-side (see Statistics.
// GetExtensionsAsync) - only fetched when the user actually opens this panel, and refetched every time
// rather than cached, so switching to a different database never shows stale numbers.
async function openStats() {
	el.statsModal.hidden = false;
	el.statsBody.innerHTML = `<p class="stats-loading">Loading&hellip;</p>`;

	startNavProgress();

	let data;
	try {
		data = await fetchJson("/api/stats");
	} catch (err) {
		el.statsBody.innerHTML = `<p class="stats-error">${escapeHtml(err.message || "Could not load statistics.")}</p>`;
		finishNavProgress();
		return;
	}

	finishNavProgress();
	renderStats(data);
}

function renderStats(data) {
	const extensions = data.extensions || [];

	// Bucket every extension into its media category (see FILE_CATEGORIES - the same table that already
	// drives each file's icon), so the pie reflects exactly the same categorization the file list itself
	// uses. Anything matching no category (and the "no extension" bucket) falls into "Other".
	const categoryTotals = new Map();

	for (const entry of extensions) {
		const bareExtension = (entry.extension || "").replace(/^\./, "").toLowerCase();
		const category = findFileCategory(bareExtension);
		const key = category ? category.name : "Other";

		const totals = categoryTotals.get(key) || { count: 0, size: 0 };
		totals.count += entry.fileCount;
		totals.size += entry.totalSize;
		categoryTotals.set(key, totals);
	}

	const categoryEntries = [...categoryTotals.entries()]
		.filter(([, totals]) => totals.count > 0)
		.sort((a, b) => b[1].size - a[1].size);

	const columns = document.createElement("div");
	columns.className = "stats-columns";
	columns.appendChild(buildStatsPieSection(categoryEntries));
	columns.appendChild(buildStatsExtensionsTable(extensions));

	el.statsBody.innerHTML = "";
	el.statsBody.appendChild(buildStatsSummary());
	el.statsBody.appendChild(columns);
	el.statsBody.appendChild(buildStatsSpeedtestSection(data.speedtest));
}

function buildStatsSummary() {
	const wrap = document.createElement("div");
	wrap.className = "stats-summary";

	const items = [
		["Total files", formatCount(state.totalFiles)],
		["Total size", formatBytes(state.totalSize)],
		["Directories", formatCount(state.totalDirectories)],
		["Database size", formatBytes(state.databaseSizeBytes)],
	];

	for (const [label, value] of items) {
		const item = document.createElement("div");
		item.className = "stats-summary-item";
		item.innerHTML = `<div class="value">${escapeHtml(value)}</div><div class="label">${escapeHtml(label)}</div>`;
		wrap.appendChild(item);
	}

	return wrap;
}

function buildStatsPieSection(categoryEntries) {
	const section = document.createElement("div");

	const title = document.createElement("h3");
	title.className = "stats-section-title";
	title.textContent = "Media types";
	section.appendChild(title);

	const row = document.createElement("div");
	row.className = "stats-pie-row";

	if (categoryEntries.length === 0) {
		row.innerHTML = `<p class="stats-empty">No files to categorize yet.</p>`;
		section.appendChild(row);
		return section;
	}

	const totalSize = categoryEntries.reduce((sum, [, totals]) => sum + totals.size, 0);

	// Weight slices by size (what the user asked to see at a glance), but give every category at least a
	// sliver of the circle - a category made up entirely of 0-byte files would otherwise be invisible here
	// despite having its own legend row right below it.
	const weights = categoryEntries.map(([, totals]) => (totalSize > 0 ? Math.max(totals.size / totalSize, 0.006) : 1 / categoryEntries.length));
	const weightSum = weights.reduce((sum, weight) => sum + weight, 0);

	let cursor = 0;
	const stops = categoryEntries.map(([name], index) => {
		const start = (cursor / weightSum) * 100;
		cursor += weights[index];
		const end = (cursor / weightSum) * 100;
		return `${CATEGORY_COLORS.get(name) || "#9099a6"} ${start}% ${end}%`;
	});

	const pie = document.createElement("div");
	pie.className = "pie-chart";
	pie.style.background = `conic-gradient(${stops.join(", ")})`;
	row.appendChild(pie);

	const legend = document.createElement("ul");
	legend.className = "pie-legend";

	for (const [name, totals] of categoryEntries) {
		const item = document.createElement("li");
		item.innerHTML = `<span class="pie-swatch" style="background:${CATEGORY_COLORS.get(name) || "#9099a6"}"></span><span>${escapeHtml(name)} - ${escapeHtml(formatBytes(totals.size))} <span class="count">(${formatCount(totals.count)} file${totals.count === 1 ? "" : "s"})</span></span>`;
		legend.appendChild(item);
	}

	row.appendChild(legend);
	section.appendChild(row);

	return section;
}

function buildStatsExtensionsTable(extensions) {
	const section = document.createElement("div");

	const title = document.createElement("h3");
	title.className = "stats-section-title";
	title.textContent = "Top 10 file extensions";
	section.appendChild(title);

	const top10 = [...extensions].sort((a, b) => b.fileCount - a.fileCount).slice(0, 10);

	if (top10.length === 0) {
		const empty = document.createElement("p");
		empty.className = "stats-empty";
		empty.textContent = "No files found.";
		section.appendChild(empty);
		return section;
	}

	const table = document.createElement("table");
	table.className = "stats-table";
	table.innerHTML = "<thead><tr><th>Extension</th><th>Files</th><th>Size</th></tr></thead>";

	const tbody = document.createElement("tbody");

	for (const entry of top10) {
		const label = entry.extension ? entry.extension : "(no extension)";
		const row = document.createElement("tr");
		row.innerHTML = `<td>${escapeHtml(label)}</td><td>${formatCount(entry.fileCount)}</td><td>${escapeHtml(formatBytes(entry.totalSize))}</td>`;
		tbody.appendChild(row);
	}

	table.appendChild(tbody);
	section.appendChild(table);

	return section;
}

// A download of the scan's largest known file, timed to find the site's peak sustained throughput - see
// --speedtest / OpenDirectoryIndexer. Reuses the same big-number tile look as buildStatsSummary, since this
// is just a few more scan-wide totals, not a different kind of data.
function buildStatsSpeedtestSection(speedtest) {
	const section = document.createElement("div");

	const title = document.createElement("h3");
	title.className = "stats-section-title";
	title.textContent = "Speedtest";
	section.appendChild(title);
	section.className = "stats-speedtest";

	if (!speedtest) {
		const empty = document.createElement("p");
		empty.className = "stats-empty";
		empty.textContent = "No speedtest recorded for this scan - run with --speedtest to include one.";
		section.appendChild(empty);
		return section;
	}

	// DownloadedBytes of 0 means a speedtest was attempted but failed (see OpenDirectoryIndexer) - mirrors
	// the same "Failed" reporting the CLI's own end-of-scan summary uses (see Statistics.GetSessionStats).
	if (speedtest.downloadedBytes <= 0) {
		const failed = document.createElement("p");
		failed.className = "stats-empty";
		failed.textContent = "Speedtest failed.";
		section.appendChild(failed);
		return section;
	}

	// MaxBytesPerSecond is the best one-second window observed during the test (see Library.
	// SpeedtestFromStream), not a simple average over the whole download - same "peak" metric the CLI
	// reports, just also broken out as a bitrate here.
	const peakMBs = speedtest.maxBytesPerSecond / (1024 * 1024);
	const peakMbit = peakMBs * 8;

	const wrap = document.createElement("div");
	wrap.className = "stats-summary";

	const items = [
		["Downloaded", formatBytes(speedtest.downloadedBytes)],
		["Duration", `${(speedtest.elapsedMilliseconds / 1000).toFixed(1)}s`],
		["Peak speed", `${formatBytes(speedtest.maxBytesPerSecond)}/s`],
		["Peak bitrate", `${peakMbit.toFixed(1)} mbit/s`],
	];

	for (const [label, value] of items) {
		const item = document.createElement("div");
		item.className = "stats-summary-item";
		item.innerHTML = `<div class="value">${escapeHtml(value)}</div><div class="label">${escapeHtml(label)}</div>`;
		wrap.appendChild(item);
	}

	section.appendChild(wrap);

	return section;
}

// A thin bar across the top of the page (classic "nprogress" look) - directory listings can take a
// noticeable moment on a large database, so every navigation (breadcrumb, folder click, Up, back/forward)
// gets this instead of the page just sitting there looking unresponsive. There's no real progress to
// report (the request is a single fetch), so it eases toward - never reaches - 70% while waiting, then
// snaps to 100% and fades out once the response arrives.
let navProgressHideTimer = null;

function startNavProgress() {
	clearTimeout(navProgressHideTimer);

	el.navProgress.hidden = false;
	el.navProgress.classList.remove("done");
	void el.navProgress.offsetWidth; // flush the "done"/hidden state so the 0%->70% transition below actually animates
	el.navProgress.classList.add("active");
}

function finishNavProgress() {
	el.navProgress.classList.remove("active");
	el.navProgress.classList.add("done");

	navProgressHideTimer = setTimeout(() => {
		el.navProgress.hidden = true;
		el.navProgress.classList.remove("done");
	}, 300);
}

async function loadDirectory(url, { pushState = true, replace = false } = {}) {
	// Resets the page scroll position too, not just the entry list's own (see renderEntryRows) - otherwise
	// clicking "Open" from somewhere further down the page (e.g. a global search result) leaves the viewport
	// wherever it was instead of showing the top of the newly-loaded directory.
	window.scrollTo(0, 0);

	startNavProgress();

	let data;
	try {
		data = await fetchJson(`/api/directory?url=${encodeURIComponent(url)}`);
	} catch (err) {
		showToast(err.message || "Could not load that directory.");
		return false;
	} finally {
		finishNavProgress();
	}

	state.currentUrl = url;
	state.currentParentUrl = data.parentUrl;
	state.currentEntries = data.entries;

	el.emptyState.hidden = true;
	el.browser.hidden = false;
	el.toolbar.hidden = false;
	el.upBtn.hidden = !data.parentUrl;

	renderBreadcrumbs(data);
	renderSummary(data);
	renderEntries(true);

	// Re-apply the already-fetched global search results (no new request) against the newly-loaded
	// folder, since which matches count as "elsewhere" depends on state.currentUrl.
	if (lastGlobalResult) {
		renderGlobalResults(lastGlobalResult);
	}

	const term = state.filterTerm.trim();

	if (replace) {
		history.replaceState({ url }, "", buildLocation(url, term));
	} else if (pushState) {
		history.pushState({ url }, "", buildLocation(url, term));
	}

	return true;
}

function renderBreadcrumbs(data) {
	el.breadcrumbs.innerHTML = "";

	// Built from the database's actual parent/child links (data.ancestors, computed server-side), not by
	// splitting the directory URL as a path - that breaks for sites whose URLs aren't simple hierarchical
	// paths, or (like Google Drive folder names) contain characters a naive path-split can't handle.
	const crumbs = [...(data.ancestors || []), { url: data.url, name: data.name }];

	// The root directory's own recorded Name is often just a generic placeholder (e.g. "ROOT"), not
	// anything that identifies the site - show the actual root URL there instead.
	if (crumbs.length > 0) {
		crumbs[0] = { ...crumbs[0], name: state.rootUrl };
	}

	crumbs.forEach((crumb, index) => {
		if (index > 0) {
			const sep = document.createElement("span");
			sep.className = "sep";
			// Omit the "/" itself (keeping the element for its spacing) when the previous crumb's own
			// label already ends in one (true for the root URL) - otherwise it reads as a duplicated
			// slash, e.g. "http://site/ / folder".
			sep.textContent = crumbs[index - 1].name.endsWith("/") ? "" : "/";
			el.breadcrumbs.appendChild(sep);
		}

		if (index === crumbs.length - 1) {
			const span = document.createElement("span");
			span.textContent = crumb.name;
			el.breadcrumbs.appendChild(span);
		} else {
			const a = document.createElement("a");
			a.href = buildLocation(crumb.url, state.filterTerm.trim());
			a.textContent = crumb.name;
			a.addEventListener("click", (e) => handleNavigationClick(e, crumb.url));
			el.breadcrumbs.appendChild(a);
		}
	});
}

function renderSummary(data) {
	const directoryCount = data.entries.filter((entry) => entry.type === "directory").length;
	const fileCount = data.entries.filter((entry) => entry.type === "file").length;

	el.summary.textContent = `${formatBytes(data.totalSize)} total - ${formatCount(directoryCount)} folder${directoryCount === 1 ? "" : "s"}, ${formatCount(fileCount)} file${fileCount === 1 ? "" : "s"}`;
}

function renderEntries(resetScroll = false) {
	const term = state.filterTerm.trim().toLowerCase();
	const filtered = term ? state.currentEntries.filter((entry) => entry.name.toLowerCase().includes(term)) : state.currentEntries;

	const directories = sortEntries(filtered.filter((entry) => entry.type === "directory"));
	const files = sortEntries(filtered.filter((entry) => entry.type === "file"));
	const sorted = [...directories, ...files];

	// Bar widths stay relative to the whole folder (not just the filtered subset), so a filtered-down
	// small file doesn't misleadingly render with a full-width bar.
	const maxSize = Math.max(1, ...state.currentEntries.map((entry) => entry.size || 0));
	const galleryOn = el.galleryToggle.checked;

	const imageEntries = files.filter((entry) => isImage(entry.name));
	const listEntries = galleryOn ? sorted.filter((entry) => !(entry.type === "file" && isImage(entry.name))) : sorted;

	el.gallery.hidden = !(galleryOn && imageEntries.length > 0);
	el.gallery.innerHTML = "";

	if (galleryOn) {
		for (const entry of imageEntries) {
			const a = document.createElement("a");
			a.className = "gallery-item";
			a.href = `/api/download-file?url=${encodeURIComponent(entry.url)}`;
			a.target = "_blank";
			a.rel = "noopener";

			const img = document.createElement("img");
			img.loading = "lazy";
			img.src = `/api/download-file?url=${encodeURIComponent(entry.url)}`;
			img.alt = entry.name;

			const caption = document.createElement("div");
			caption.className = "caption";
			caption.textContent = entry.name;

			a.appendChild(img);
			a.appendChild(caption);
			el.gallery.appendChild(a);
		}
	}

	renderEntryRows(el.entryList, listEntries, maxSize, resetScroll);
}

// How many entries can realistically be in a scanned directory ("very large open directory" is this app's
// whole reason to exist) is why the row list is virtualized - only the rows within the scrollable area
// (plus a small buffer) are ever in the DOM at once, no matter how long the list is. Rendering is delegated
// to Clusterize.js (loaded from a CDN, see index.html), which expects an array of HTML strings rather than
// DOM nodes, so entry rows are serialized to HTML (see buildEntryRowHtml) and all click handling below is
// done via one delegated listener on the content element instead of per-row listeners.
let entryClusterize = null;
let entryHeaderEl = null;
let entryScrollElem = null;

function renderEntryRows(container, entries, maxSize, resetScroll) {
	const rows = entries.map((entry) => buildEntryRowHtml(entry, maxSize));

	if (entryClusterize) {
		entryHeaderEl.replaceWith((entryHeaderEl = buildEntryListHeader()));

		// The scroll container is a single persistent DOM node reused across every directory (only
		// .update()'s content changes) - the browser never scrolls it back to the top on its own, it only
		// clamps scrollTop down if the new folder's content happens to be shorter than the old scroll
		// position. Reset it explicitly before updating, so Clusterize computes its visible window from the
		// top of the new folder's rows, not wherever the previous folder happened to be scrolled to.
		if (resetScroll) {
			entryScrollElem.scrollTop = 0;
		}

		entryClusterize.update(rows);
		return;
	}

	container.innerHTML = "";
	container.appendChild((entryHeaderEl = buildEntryListHeader()));

	entryScrollElem = document.createElement("div");
	entryScrollElem.className = "entry-list-scroll";

	const contentElem = document.createElement("div");
	contentElem.className = "clusterize-content";
	contentElem.addEventListener("click", onEntryListClick);

	entryScrollElem.appendChild(contentElem);
	container.appendChild(entryScrollElem);

	entryClusterize = new Clusterize({
		rows,
		scrollElem: entryScrollElem,
		contentElem,
		tag: "div",
		no_data_text: "No files or folders.",
	});
}

// Rows are plain HTML strings (see buildEntryRowHtml), so clicks are handled here via delegation on the
// shared content element instead of per-row listeners - this mirrors the two listeners the old DOM-node
// version attached directly to each row/link.
function onEntryListClick(e) {
	const zipBtn = e.target.closest(".zip-btn");
	if (zipBtn) {
		e.stopPropagation();
		downloadZip(zipBtn.dataset.url);
		return;
	}

	const navLink = e.target.closest("a.entry-nav");
	if (navLink) {
		handleNavigationClick(e, navLink.dataset.url);
		return;
	}

	if (e.target.closest(".btn") || e.target.closest("a")) {
		return; // handled by the link/button's own href, or already handled above
	}

	const row = e.target.closest(".entry-row--directory");
	if (!row) {
		return;
	}

	if (!isPlainLeftClick(e)) {
		window.open(buildLocation(row.dataset.url, ""), "_blank");
		return;
	}

	loadDirectory(row.dataset.url);
}

function sortEntries(entries) {
	const direction = state.sortDirection === "desc" ? -1 : 1;

	return [...entries].sort((a, b) => {
		if (state.sortField === "size") {
			return ((a.size || 0) - (b.size || 0)) * direction;
		}

		return a.name.localeCompare(b.name, undefined, { numeric: true, sensitivity: "base" }) * direction;
	});
}

function buildEntryListHeader() {
	const header = document.createElement("div");
	header.className = "entry-list-header";

	header.appendChild(buildSortButton("name", "Name"));
	header.appendChild(document.createElement("span"));
	header.appendChild(buildSortButton("size", "Size"));
	header.appendChild(document.createElement("span"));

	return header;
}

function buildSortButton(field, label) {
	const button = document.createElement("button");
	button.type = "button";
	button.className = "sort-btn";

	const isActive = state.sortField === field;
	if (isActive) {
		button.classList.add("active");
	}

	button.textContent = label + (isActive ? (state.sortDirection === "desc" ? " ▼" : " ▲") : "");

	button.addEventListener("click", () => {
		if (state.sortField === field) {
			state.sortDirection = state.sortDirection === "asc" ? "desc" : "asc";
		} else {
			state.sortField = field;
			state.sortDirection = field === "size" ? "desc" : "asc";
		}

		renderEntries();
	});

	return button;
}

const HTML_ESCAPES = { "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" };

function escapeHtml(value) {
	return String(value ?? "").replace(/[&<>"']/g, (ch) => HTML_ESCAPES[ch]);
}

function buildEntryRowHtml(entry, maxSize) {
	const isDirectory = entry.type === "directory";
	const safeUrl = escapeHtml(entry.url);
	const encodedUrl = encodeURIComponent(entry.url);

	const icon = isDirectory ? "\u{1F4C1}" : getFileIcon(entry);

	let badgeHtml = "";
	if (isDirectory) {
		if (!entry.finished) {
			badgeHtml += `<span class="entry-badge">in progress</span>`;
		}
		if (entry.error) {
			badgeHtml += `<span class="entry-badge error">error</span>`;
		}
	} else if (getDriveTypeIcon(entry)) {
		badgeHtml += `<span class="entry-badge">${escapeHtml(entry.description)}</span>`;
	}

	const size = entry.size || 0;
	const barWidth = Math.max(size > 0 ? 2 : 0, (size / maxSize) * 100);

	const sizeLine = entry.type === "file" && entry.unknownSize ? "unknown" : escapeHtml(formatBytes(entry.size || 0));
	const countLine = isDirectory
		? `<div class="entry-size-sub">${formatCount(entry.fileCount || 0)} file${entry.fileCount === 1 ? "" : "s"}</div>`
		: "";

	const nameHref = isDirectory ? escapeHtml(buildLocation(entry.url, state.filterTerm.trim())) : `/api/download-file?url=${encodedUrl}`;

	const actionsHtml = isDirectory
		? `<button type="button" class="btn secondary zip-btn" data-url="${safeUrl}">ZIP</button>`
		: `<a class="btn" href="/api/download-file?url=${encodedUrl}">Download</a>`;

	return `<div class="entry-row ${isDirectory ? "entry-row--directory" : "entry-row--file"}"${isDirectory ? ` data-url="${safeUrl}" style="cursor:pointer"` : ""}>
	<div class="entry-name">
		<span class="entry-icon">${icon}</span>
		<a class="label${isDirectory ? " entry-nav" : ""}" href="${nameHref}" data-url="${safeUrl}">${escapeHtml(entry.name)}</a>
		${badgeHtml}
	</div>
	<div class="entry-bar-wrap">
		<div class="entry-bar"><span style="width: ${barWidth}%"></span></div>
	</div>
	<div class="entry-size">
		<div>${sizeLine}</div>
		${countLine}
	</div>
	<div class="entry-actions">
		${actionsHtml}
		${buildOriginalUrlLinkHtml(entry.url)}
	</div>
</div>`;
}

// Opens the real, original URL on the scanned site directly - not our own /api/download-file proxy. Useful
// e.g. to view a Google Drive file/folder in Drive's own viewer instead of downloading it through us.
function buildOriginalUrlLinkHtml(url) {
	return `<a class="btn secondary icon-btn" href="${escapeHtml(url)}" target="_blank" rel="noopener" title="Open original URL" aria-label="Open original URL">\u{1F517}</a>`;
}

function badge(text, isError) {
	const span = document.createElement("span");
	span.className = isError ? "entry-badge error" : "entry-badge";
	span.textContent = text;
	return span;
}

// Opens the real, original URL on the scanned site directly - not our own /api/download-file proxy. Useful
// e.g. to view a Google Drive file/folder in Drive's own viewer instead of downloading it through us.
function buildOriginalUrlLink(url) {
	const link = document.createElement("a");
	link.className = "btn secondary icon-btn";
	link.href = url;
	link.target = "_blank";
	link.rel = "noopener";
	link.title = "Open original URL";
	link.setAttribute("aria-label", "Open original URL");
	link.textContent = "\u{1F517}";
	return link;
}

function isImage(name) {
	const dot = name.lastIndexOf(".");
	if (dot === -1) {
		return false;
	}
	return IMAGE_EXTENSIONS.has(name.slice(dot + 1).toLowerCase());
}

function renderGlobalResults(result) {
	const currentUrl = state.currentUrl;

	// Matches already visible in the current folder's (filtered) listing above are excluded here so
	// nothing is shown twice on screen.
	const files = result.files.filter((match) => match.directoryUrl !== currentUrl);
	const directories = result.directories.filter((match) => match.parentUrl !== currentUrl);

	if (files.length === 0 && directories.length === 0) {
		clearGlobalResults();
		return;
	}

	el.globalResults.hidden = false;
	el.globalResultsList.innerHTML = "";

	for (const match of directories) {
		el.globalResultsList.appendChild(buildResultRow({
			type: "directory",
			url: match.url,
			name: match.name,
			path: match.parentUrl,
			finished: match.finished,
			error: match.error,
		}));
	}

	for (const match of files) {
		el.globalResultsList.appendChild(buildResultRow({
			type: "file",
			url: match.url,
			name: match.name,
			size: match.size,
			unknownSize: match.unknownSize,
			path: match.directoryUrl,
		}));
	}

	el.globalResultsNote.hidden = !result.truncated;
	el.globalResultsNote.textContent = result.truncated ? "Showing the first matches only - refine your search for more precise results." : "";
}

function buildResultRow(entry) {
	const row = document.createElement("div");
	row.className = "result-row";

	const nameCell = document.createElement("div");
	nameCell.className = "entry-name";

	const icon = document.createElement("span");
	icon.className = "entry-icon";
	icon.textContent = entry.type === "directory" ? "\u{1F4C1}" : getFileIcon(entry);
	nameCell.appendChild(icon);

	const textWrap = document.createElement("div");
	textWrap.className = "result-text";

	const labelRow = document.createElement("div");
	labelRow.className = "result-label-row";

	const label = document.createElement("a");
	label.className = "label";
	label.textContent = entry.name;

	if (entry.type === "directory") {
		label.href = buildLocation(entry.url, state.filterTerm.trim());
		label.addEventListener("click", (e) => handleNavigationClick(e, entry.url));
	} else {
		label.href = `/api/download-file?url=${encodeURIComponent(entry.url)}`;
	}

	labelRow.appendChild(label);

	if (entry.type === "directory") {
		if (!entry.finished) {
			labelRow.appendChild(badge("in progress"));
		}
		if (entry.error) {
			labelRow.appendChild(badge("error", true));
		}
	} else if (getDriveTypeIcon(entry)) {
		labelRow.appendChild(badge(entry.description));
	}

	textWrap.appendChild(labelRow);

	const path = document.createElement("span");
	path.className = "result-path";
	path.textContent = entry.path || "";
	textWrap.appendChild(path);

	nameCell.appendChild(textWrap);

	const sizeCell = document.createElement("div");
	sizeCell.className = "entry-size";
	sizeCell.textContent = entry.type === "file" ? (entry.unknownSize ? "unknown" : formatBytes(entry.size || 0)) : "";

	const actionsCell = document.createElement("div");
	actionsCell.className = "entry-actions";

	if (entry.type === "directory") {
		const openLink = document.createElement("a");
		openLink.className = "btn secondary";
		openLink.href = buildLocation(entry.url, state.filterTerm.trim());
		openLink.textContent = "Open";
		openLink.addEventListener("click", (e) => handleNavigationClick(e, entry.url));
		actionsCell.appendChild(openLink);
	} else {
		const showLink = document.createElement("a");
		showLink.className = "btn secondary";
		showLink.href = buildLocation(entry.path, state.filterTerm.trim());
		showLink.textContent = "Show in folder";
		showLink.addEventListener("click", (e) => handleNavigationClick(e, entry.path));
		actionsCell.appendChild(showLink);

		const downloadLink = document.createElement("a");
		downloadLink.className = "btn";
		downloadLink.href = `/api/download-file?url=${encodeURIComponent(entry.url)}`;
		downloadLink.textContent = "Download";
		actionsCell.appendChild(downloadLink);
	}

	actionsCell.appendChild(buildOriginalUrlLink(entry.url));

	row.appendChild(nameCell);
	row.appendChild(sizeCell);
	row.appendChild(actionsCell);

	return row;
}

function downloadZip(url) {
	const jobId = crypto.randomUUID();

	const link = document.createElement("a");
	link.href = `/api/download-zip?url=${encodeURIComponent(url)}&jobId=${encodeURIComponent(jobId)}`;
	link.style.display = "none";
	document.body.appendChild(link);
	link.click();
	link.remove();

	watchZipProgress(jobId);
}

function watchZipProgress(jobId) {
	const source = new EventSource(`/api/zip-progress/${encodeURIComponent(jobId)}`);

	el.progressPanel.hidden = false;
	el.progressTitle.textContent = "Building ZIP…";
	el.progressBarFill.style.width = "0%";
	el.progressDetail.textContent = "";

	source.onmessage = (event) => {
		const progress = JSON.parse(event.data);

		if (progress.error) {
			el.progressTitle.textContent = "ZIP failed";
			el.progressDetail.textContent = progress.error;
			source.close();
			setTimeout(() => (el.progressPanel.hidden = true), 4000);
			return;
		}

		const percent = progress.totalFiles > 0 ? Math.round((progress.filesDone / progress.totalFiles) * 100) : 0;
		el.progressBarFill.style.width = `${percent}%`;

		const skippedText = progress.skipped > 0 ? `, ${progress.skipped} skipped` : "";
		el.progressDetail.textContent = `${progress.filesDone} / ${progress.totalFiles} files${skippedText} - ${formatBytes(progress.bytesWritten)}${progress.currentFile ? ` - ${progress.currentFile}` : ""}`;

		if (progress.isComplete) {
			el.progressTitle.textContent = "ZIP ready";
			source.close();
			setTimeout(() => (el.progressPanel.hidden = true), 3000);
		}
	};

	source.onerror = () => {
		source.close();
	};
}

function showToast(message) {
	el.toast.textContent = message;
	el.toast.hidden = false;
	setTimeout(() => (el.toast.hidden = true), 4000);
}

async function fetchJson(url) {
	const response = await fetch(url);
	const body = await response.json();

	if (!response.ok) {
		throw new Error(body.error || `Request failed (${response.status}).`);
	}

	return body;
}

function formatCount(value) {
	// An explicit locale is required here: toLocaleString() / toLocaleString(undefined) silently skips
	// thousands grouping in some browser builds even when navigator.language is "en-US" - only naming
	// the locale explicitly reliably applies it.
	return value.toLocaleString("en-US");
}

function formatBytes(bytes) {
	if (!bytes || bytes <= 0) {
		return "0 B";
	}

	const units = ["B", "KB", "MB", "GB", "TB", "PB"];
	const exponent = Math.min(units.length - 1, Math.floor(Math.log(bytes) / Math.log(1024)));
	const value = bytes / Math.pow(1024, exponent);

	return `${exponent === 0 ? value : value.toFixed(value < 10 ? 2 : 1)} ${units[exponent]}`;
}