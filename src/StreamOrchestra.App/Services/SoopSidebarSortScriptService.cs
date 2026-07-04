namespace StreamOrchestra.App.Services;

public static class SoopSidebarSortScriptService
{
    public static string CreateScript()
    {
        return """
(() => {
  const installedKey = "__streamOrchestraSoopSidebarSortInstalled";
  if (window[installedKey]) {
    return;
  }

  const allowedHosts = ["sooplive.co.kr", "sooplive.com", "play.sooplive.com"];
  const host = window.location.hostname.toLowerCase();
  if (!allowedHosts.some(allowedHost => host === allowedHost || host.endsWith(`.${allowedHost}`))) {
    return;
  }

  window[installedKey] = true;

  let sortTimer = 0;
  let isSorting = false;

  function parseViewerCount(text) {
    const value = String(text || "")
      .replace(/,/g, "")
      .replace(/\s+/g, " ")
      .trim();
    const matches = [
      ...value.matchAll(/(\d+(?:\.\d+)?)\s*(억|만)?\s*(명|시청자|시청|viewer|viewers)/gi),
      ...value.matchAll(/(\d+(?:\.\d+)?)\s*(억|만)/g)
    ];
    let best = -1;

    for (const match of matches) {
      const raw = Number.parseFloat(match[1]);
      if (!Number.isFinite(raw)) {
        continue;
      }

      const unit = match[2] || "";
      const multiplier = unit === "억" ? 100000000 : unit === "만" ? 10000 : 1;
      best = Math.max(best, Math.round(raw * multiplier));
    }

    return best;
  }

  function findViewerCount(item) {
    const candidates = [
      ...item.querySelectorAll([
        "[class*='viewer' i]",
        "[class*='view' i]",
        "[class*='count' i]",
        "[class*='watch' i]",
        "[aria-label*='시청']",
        "[title*='시청']"
      ].join(","))
    ];

    for (const candidate of candidates) {
      const parsed = Math.max(
        parseViewerCount(candidate.textContent),
        parseViewerCount(candidate.getAttribute("aria-label")),
        parseViewerCount(candidate.getAttribute("title"))
      );
      if (parsed >= 0) {
        return parsed;
      }
    }

    return parseViewerCount(item.textContent);
  }

  function hasSidebarSectionText(element) {
    const text = (element.textContent || "").replace(/\s+/g, "");
    return text.includes("즐겨찾기") || text.includes("추천");
  }

  function belongsToSortableSidebarSection(element) {
    let current = element;
    for (let depth = 0; current && depth < 6; depth += 1) {
      if (hasSidebarSectionText(current)) {
        return true;
      }

      current = current.parentElement;
    }

    return false;
  }

  function isLikelyStreamItem(element) {
    return element instanceof HTMLElement &&
      element.querySelector("a[href]") &&
      findViewerCount(element) >= 0;
  }

  function sortContainer(container) {
    const items = Array.from(container.children)
      .map((element, index) => ({
        element,
        index,
        viewerCount: findViewerCount(element)
      }))
      .filter(item => item.viewerCount >= 0 && isLikelyStreamItem(item.element));

    if (items.length < 2) {
      return false;
    }

    const originalItems = [...items];
    items.sort((left, right) => right.viewerCount - left.viewerCount);
    const sorted = items;
    if (sorted.every((item, index) => item.element === originalItems[index].element)) {
      return false;
    }

    for (const item of sorted) {
      container.appendChild(item.element);
    }

    return true;
  }

  function findSortableContainers() {
    const selectors = [
      "ul",
      "ol",
      "[role='list']",
      "[class*='list' i]",
      "[class*='favorite' i]",
      "[class*='recommend' i]",
      "[class*='sidebar' i]"
    ].join(",");

    return [...document.querySelectorAll(selectors)]
      .filter(element => element instanceof HTMLElement)
      .filter(belongsToSortableSidebarSection)
      .filter(element => Array.from(element.children).filter(isLikelyStreamItem).length >= 2);
  }

  function sortNow() {
    if (isSorting) {
      return;
    }

    isSorting = true;
    try {
      for (const container of findSortableContainers()) {
        sortContainer(container);
      }
    } catch {
      // SOOP changes its markup periodically; sorting must never block normal browsing.
    } finally {
      isSorting = false;
    }
  }

  function requestSort(reason) {
    window.clearTimeout(sortTimer);
    const delay = reason === "click" ? 250 : 100;
    sortTimer = window.setTimeout(sortNow, delay);
  }

  const observer = new MutationObserver(() => requestSort("mutation"));
  observer.observe(document.documentElement, {
    childList: true,
    subtree: true
  });

  document.addEventListener("click", event => {
    const target = event.target instanceof Element ? event.target : null;
    const label = target?.closest?.("button, a, [role='button']")?.textContent || "";
    if (/더보기|새로고침|refresh|more/i.test(label)) {
      requestSort("click");
      window.setTimeout(() => requestSort("click"), 800);
    }
  }, true);

  requestSort("initial");
})();
""";
    }
}
