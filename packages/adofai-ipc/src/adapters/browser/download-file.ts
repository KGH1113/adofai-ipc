import { IpcCallError } from "../../api/errors";
import type { FileDownloader } from "../../ports/file-downloader";

/** The runtime serves an attachment. Navigation lets the browser stream it directly to disk. */
export const downloadBrowserFile: FileDownloader = ({ url, signal }) => {
  if (signal.aborted) throw signal.reason;
  if (typeof document === "undefined" || !document.body)
    throw new IpcCallError("file_download_unavailable", "Open this app in a browser before downloading the file.");

  const frame = document.createElement("iframe");
  frame.hidden = true;
  frame.tabIndex = -1;
  frame.setAttribute("aria-hidden", "true");
  frame.referrerPolicy = "no-referrer";
  const cleanup = () => {
    signal.removeEventListener("abort", cleanup);
    frame.removeEventListener("load", cleanup);
    frame.remove();
  };
  // An attachment navigation may never fire load. Keep it alive until the session ends,
  // rather than using a timer that could interrupt a large or slow download.
  signal.addEventListener("abort", cleanup, { once: true });
  frame.addEventListener("load", cleanup, { once: true });
  try {
    frame.src = url;
    document.body.append(frame);
  } catch (error) {
    cleanup();
    throw error;
  }
};
