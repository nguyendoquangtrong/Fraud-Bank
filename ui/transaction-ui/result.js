const LOOKUP_API_BASE = "http://localhost:5272";
const FINAL_STATUSES = new Set(["LEDGER_APPLIED", "DECIDED_BLOCK"]);
const PENDING_STATUSES = new Set(["DECIDED_REVIEW", "REQUESTED"]);

const params = new URLSearchParams(window.location.search);

const sanitizeStatus = (value) => {
    if (!value) return "";
    return value.replace(/["']/g, "").trim().toUpperCase();
};

const viewState = {
    txId: params.get("txId") ?? "",
    status: sanitizeStatus(params.get("status")) || "REQUESTED",
    from: params.get("from") ?? "",
    to: params.get("to") ?? "",
    amount: params.get("amount") ?? "",
    message: params.get("message") ?? ""
};

const statusIcon = document.getElementById("statusIcon");
const statusTitle = document.getElementById("statusTitle");
const statusSubtitle = document.getElementById("statusSubtitle");
const statusMessage = document.getElementById("statusMessage");
const detailTxId = document.getElementById("detailTxId");
const detailFrom = document.getElementById("detailFrom");
const detailTo = document.getElementById("detailTo");
const detailAmount = document.getElementById("detailAmount");

const formatAmount = (value) => {
    const asNumber = Number(value);
    if (!Number.isFinite(asNumber)) return value || "-";
    return new Intl.NumberFormat("vi-VN", {
        style: "currency",
        currency: "VND"
    }).format(asNumber);
};

const applyStatus = () => {
    const status = sanitizeStatus(viewState.status);

    statusIcon.classList.remove("success", "error", "spinner");
    statusIcon.textContent = "";

    let message = viewState.message?.trim() ?? "";

    if (status === "LEDGER_APPLIED") {
        statusIcon.textContent = "✓";
        statusIcon.classList.add("success");
        statusTitle.textContent = "Giao dịch hoàn tất";
        statusSubtitle.textContent = "Tiền đã được hạch toán thành công.";
        if (!message) message = "Giao dịch đã hoàn tất.";
    } else if (status === "DECIDED_BLOCK") {
        statusIcon.textContent = "!";
        statusIcon.classList.add("error");
        statusTitle.textContent = "Giao dịch bị từ chối";
        statusSubtitle.textContent = "Hệ thống xác định giao dịch có rủi ro cao.";
        if (!message) message = "Vui lòng liên hệ nhân viên hỗ trợ để biết thêm chi tiết.";
    } else if (PENDING_STATUSES.has(status)) {
        statusIcon.classList.add("spinner");
        statusTitle.textContent = "Giao dịch đang chờ duyệt";
        statusSubtitle.textContent = "Hệ thống chống gian lận cần thêm thời gian để xem xét.";
        if (!message) message = "Bạn sẽ nhận được cập nhật ngay khi có quyết định cuối cùng.";
    } else {
        statusIcon.textContent = "?";
        statusTitle.textContent = "Chưa xác định trạng thái";
        statusSubtitle.textContent = "Chưa có thông tin trạng thái cho giao dịch này.";
        if (!message) message = "Hãy thử lại sau hoặc liên hệ bộ phận hỗ trợ.";
    }

    statusMessage.textContent = message;
    statusMessage.classList.toggle("hidden", !message);

    detailTxId.textContent = viewState.txId || "Không có";
    detailFrom.textContent = viewState.from || "-";
    detailTo.textContent = viewState.to || "-";
    detailAmount.textContent = viewState.amount ? formatAmount(viewState.amount) : "-";
};

const extractField = (payload, keys, fallback) => {
    if (!payload || typeof payload !== "object") return fallback;
    for (const key of keys) {
        if (payload[key] !== undefined && payload[key] !== null) {
            return payload[key];
        }
    }
    return fallback;
};

const schedulePoll = () => {
    if (!viewState.txId) return;
    if (FINAL_STATUSES.has(sanitizeStatus(viewState.status))) return;
    window.setTimeout(fetchLatestStatus, 5000);
};

const fetchLatestStatus = async () => {
    if (!viewState.txId) return;

    const base = LOOKUP_API_BASE.endsWith("/") ? LOOKUP_API_BASE : `${LOOKUP_API_BASE}/`;
    const endpoint = new URL(`api/transactions/${encodeURIComponent(viewState.txId)}`, base).toString();

    try {
        const res = await fetch(endpoint);
        if (!res.ok) {
            viewState.message = `Không thể lấy trạng thái (HTTP ${res.status}).`;
            applyStatus();
            schedulePoll();
            return;
        }

        const data = await res.json();
        const payload = (data && typeof data === "object")
            ? (data._source && typeof data._source === "object" ? data._source : data)
            : {};

        viewState.status = sanitizeStatus(
            extractField(payload, ["status", "Status", "state"], viewState.status)
        ) || viewState.status;

        const amountValue = extractField(payload, ["amount", "Amount", "value"], null);
        if (amountValue !== null) {
            viewState.amount = amountValue;
        }

        viewState.from = extractField(
            payload,
            ["fromAccount", "from_account", "sourceAccount", "from"],
            viewState.from
        );

        viewState.to = extractField(
            payload,
            ["toAccount", "to_account", "destinationAccount", "to"],
            viewState.to
        );

        if (!params.has("message")) {
            viewState.message = extractField(
                payload,
                ["note", "reason", "message", "StatusMessage"],
                viewState.message
            ) ?? viewState.message;
        }

        applyStatus();
        schedulePoll();
    } catch (error) {
        viewState.message = `Không thể kết nối tới dịch vụ tra cứu: ${
            error instanceof Error ? error.message : String(error)
        }`;
        applyStatus();
        schedulePoll();
    }
};

applyStatus();
fetchLatestStatus().catch(() => {
    // errors handled inside fetchLatestStatus
});
