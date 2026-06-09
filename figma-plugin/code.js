figma.showUI(__html__, { width: 380, height: 520 });

const DEFAULT_BRIDGE_URL = "http://localhost:17890/export";

figma.ui.onmessage = async (message) => {
  if (message.type === "start-bridge") {
    const projectRoot = message.projectRoot || "";
    const bridgeUrl = message.bridgeUrl || DEFAULT_BRIDGE_URL;
    const prefix = message.prefix || "http://localhost:17890/";
    const exePath = message.exePath || "FigmaUnityBridge.exe";
    const startCommand = `${exePath} --project ${quoteArg(projectRoot)} --prefix ${quoteArg(prefix)}`;
    const startupCommand = `${exePath} --install-startup --project ${quoteArg(projectRoot)} --prefix ${quoteArg(prefix)}`;

    figma.ui.postMessage({ type: "copy-command", command: startCommand });
    await waitForBridge(bridgeUrl, startCommand, startupCommand);
    return;
  }

  if (message.type === "check-bridge") {
    await checkBridge(message.bridgeUrl || DEFAULT_BRIDGE_URL);
    return;
  }

  if (message.type === "inspect-structure") {
    inspectStructure();
    return;
  }

  if (message.type === "rename-layer") {
    renameSelectedLayers(message.kind || "");
    return;
  }

  if (message.type === "create-menu-template") {
    await createMenuTemplate();
    return;
  }

  if (message.type === "export-prefab") {
    await exportPrefab(message);
  }
};

async function checkBridge(bridgeUrl) {
  try {
    const response = await fetch(getHealthUrl(bridgeUrl));
    if (!response.ok) {
      throw new Error(`${response.status} ${await response.text()}`);
    }

    const data = await response.json();
    figma.ui.postMessage({
      type: "bridge-health",
      message: `Bridge 已连接。\n工程：${data.projectRoot}\n监听：${data.prefix}`,
    });
  } catch (error) {
    figma.ui.postMessage({
      type: "error",
      message: `Bridge 未连接：${error && error.message ? error.message : String(error)}`,
    });
  }
}

async function exportPrefab(message) {
  const selection = figma.currentPage.selection;
  if (selection.length === 0) {
    figma.ui.postMessage({ type: "error", message: "请先选中一个 Figma Frame、组件或图层。" });
    return;
  }

  try {
    const bridgeUrl = message.bridgeUrl || DEFAULT_BRIDGE_URL;
    const exportTarget = message.exportTarget || "unity";
    const rootName = sanitizeName(message.prefabName || getDefaultRootName(selection));
    const request = await buildPrefabRequest(selection, rootName, message.destinationFolder || "Assets/FigmaUnityExport", {
      exportTarget,
      fontStrategy: message.fontStrategy || "unity-existing",
      unityFontGuid: message.unityFontGuid || "",
    });
    const imageCount = request.nodes.filter((node) => node.dataBase64).length;
    const textCount = request.nodes.filter((node) => node.nodeType === "TEXT").length;
    if (imageCount + textCount === 0) {
      throw new Error("选区里没有可导出的可见图片或文本图层。");
    }

    figma.ui.postMessage({
      type: "progress",
      message: `正在写入 ${getExportTargetLabel(exportTarget)}...\n图层：${request.nodes.length}，图片：${imageCount}，文本：${textCount}`,
    });

    const response = await fetch(getPrefabUrl(bridgeUrl), {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(request),
    });

    if (!response.ok) {
      throw new Error(`${response.status} ${await response.text()}`);
    }

    const result = await response.json();
    figma.ui.postMessage({
      type: "prefab-done",
      exportTarget: result.exportTarget,
      mainAssetPath: result.mainAssetPath,
      mainAssetKind: result.mainAssetKind,
      prefabPath: result.prefabPath,
      folderPath: result.folderPath,
      imageCount: result.imageCount,
      nodeCount: result.nodeCount,
      isUpdate: result.isUpdate,
      reportPath: result.reportPath,
      report: result.report,
      exportedAt: result.exportedAt,
    });
    figma.notify(`${getExportTargetLabel(result.exportTarget || exportTarget)} exported: ${result.mainAssetPath || result.prefabPath}`);
  } catch (error) {
    figma.ui.postMessage({
      type: "error",
      message: `导出 Prefab 失败：${error && error.message ? error.message : String(error)}`,
    });
  }
}

async function buildPrefabRequest(selection, rootName, destinationFolder, options) {
  const rootBounds = unionBounds(selection.map(getBounds).filter(Boolean));
  if (!rootBounds) {
    throw new Error("无法读取选区尺寸。");
  }

  const nodes = [{
    id: "figma-root",
    parentId: "",
    name: rootName,
    x: rootBounds.x,
    y: rootBounds.y,
    width: rootBounds.width,
    height: rootBounds.height,
    nodeType: "FRAME",
    visible: true,
    constraintH: "CENTER",
    constraintV: "CENTER",
    dataBase64: "",
  }];

  for (const selected of selection) {
    const selectedBounds = getBounds(selected);
    if (!selectedBounds || selected.visible === false) continue;

    if (selection.length === 1 && hasVisibleChildren(selected)) {
      for (const child of selected.children) {
        await addPrefabNode(child, "figma-root", nodes, false);
      }
    } else {
      await addPrefabNode(selected, "figma-root", nodes, false);
    }
  }

  if (nodes.filter((node) => node.dataBase64).length === 0 && selection.length === 1) {
    await addImageNode(selection[0], "figma-root", nodes, false);
  }

  return {
    rootName,
    destinationFolder,
    exportTarget: options.exportTarget || "unity",
    sourceFileKey: figma.fileKey || "",
    sourceFileName: figma.root && figma.root.name ? figma.root.name : "",
    sourcePageId: figma.currentPage ? figma.currentPage.id : "",
    sourcePageName: figma.currentPage ? figma.currentPage.name : "",
    fontStrategy: options.fontStrategy,
    unityFontGuid: options.unityFontGuid,
    nodes,
  };
}

function getExportTargetLabel(exportTarget) {
  switch ((exportTarget || "unity").toLowerCase()) {
    case "unity":
    case "unity-prefab":
    case "prefab":
      return "Unity Prefab";
    default:
      return exportTarget;
  }
}

async function addPrefabNode(node, parentId, nodes, includeHidden) {
  const forceInclude = includeHidden || isStateLayerName(node.name);
  if (node.visible === false && !forceInclude) return;
  const bounds = getBounds(node);
  if (!bounds || bounds.width <= 0 || bounds.height <= 0) return;

  if (hasExportableChildren(node, forceInclude)) {
    const id = stableNodeId(node);
    nodes.push({
      id,
      parentId,
      name: node.name || "Group",
      x: bounds.x,
      y: bounds.y,
      width: bounds.width,
      height: bounds.height,
      nodeType: node.type || "FRAME",
      visible: node.visible !== false,
      constraintH: getConstraint(node, "horizontal"),
      constraintV: getConstraint(node, "vertical"),
      dataBase64: "",
    });

    for (const child of node.children) {
      await addPrefabNode(child, id, nodes, forceInclude);
    }
    return;
  }

  await addImageNode(node, parentId, nodes, forceInclude);
}

async function addImageNode(node, parentId, nodes, includeHidden) {
  if (node.visible === false && !includeHidden) return;
  if (typeof node.exportAsync !== "function") return;
  const bounds = getBounds(node);
  if (!bounds || bounds.width <= 0 || bounds.height <= 0) return;

  if (node.type === "TEXT") {
    nodes.push({
      id: stableNodeId(node),
      parentId,
      name: node.name || "Text",
      x: bounds.x,
      y: bounds.y,
      width: bounds.width,
      height: bounds.height,
      nodeType: "TEXT",
      visible: node.visible !== false,
      constraintH: getConstraint(node, "horizontal"),
      constraintV: getConstraint(node, "vertical"),
      text: node.characters || "",
      fontSize: normalizeNumber(node.fontSize, 24),
      fontName: getFontName(node),
      color: getSolidFillColor(node),
      gradient: getTextGradient(node),
      strokeColor: getSolidStrokeColor(node),
      strokeWeight: getStrokeWeight(node),
      shadow: getDropShadow(node),
      textAlignH: node.textAlignHorizontal || "CENTER",
      textAlignV: node.textAlignVertical || "CENTER",
      wordWrap: shouldEnableWordWrap(node),
      dataBase64: "",
    });
    return;
  }

  figma.ui.postMessage({ type: "progress", message: `正在导出图层...\n${node.name}` });
  const bytes = await node.exportAsync({
    format: "PNG",
    constraint: { type: "SCALE", value: 1 },
  });

  nodes.push({
    id: stableNodeId(node),
    parentId,
    name: node.name || "Image",
    x: bounds.x,
    y: bounds.y,
    width: bounds.width,
    height: bounds.height,
    nodeType: node.type || "IMAGE",
    visible: node.visible !== false,
    constraintH: getConstraint(node, "horizontal"),
    constraintV: getConstraint(node, "vertical"),
    dataBase64: uint8ToBase64(bytes),
  });
}

function getConstraint(node, axis) {
  if (node.constraints && node.constraints[axis]) {
    return node.constraints[axis];
  }

  return axis === "horizontal" ? "CENTER" : "CENTER";
}

function normalizeNumber(value, fallback) {
  return typeof value === "number" && Number.isFinite(value) ? value : fallback;
}

function getSolidFillColor(node) {
  if (!node.fills || node.fills === figma.mixed || node.fills.length === 0) {
    return { r: 1, g: 1, b: 1, a: 1 };
  }

  const fill = node.fills.find((item) => item.type === "SOLID" && item.visible !== false);
  if (!fill) {
    return { r: 1, g: 1, b: 1, a: 1 };
  }

  return {
    r: fill.color.r,
    g: fill.color.g,
    b: fill.color.b,
    a: typeof fill.opacity === "number" ? fill.opacity : 1,
  };
}

function getTextGradient(node) {
  if (!node.fills || node.fills === figma.mixed || node.fills.length === 0) {
    return null;
  }

  const fill = node.fills.find((item) =>
    item.visible !== false &&
    (item.type === "GRADIENT_LINEAR" || item.type === "GRADIENT_RADIAL" || item.type === "GRADIENT_ANGULAR" || item.type === "GRADIENT_DIAMOND") &&
    item.gradientStops &&
    item.gradientStops.length > 0
  );
  if (!fill) {
    return null;
  }

  const stops = fill.gradientStops.slice().sort((a, b) => a.position - b.position);
  const first = gradientStopToColor(stops[0], fill.opacity);
  const last = gradientStopToColor(stops[stops.length - 1], fill.opacity);
  const direction = getGradientDirection(fill);

  if (direction === "horizontal") {
    return { topLeft: first, bottomLeft: first, topRight: last, bottomRight: last };
  }

  if (direction === "diagonal-down") {
    return { topLeft: first, topRight: first, bottomLeft: first, bottomRight: last };
  }

  if (direction === "diagonal-up") {
    return { bottomLeft: first, topLeft: first, bottomRight: last, topRight: last };
  }

  return { topLeft: first, topRight: first, bottomLeft: last, bottomRight: last };
}

function getGradientDirection(fill) {
  const handles = fill.gradientHandlePositions;
  if (handles && handles.length >= 2) {
    const dx = handles[1].x - handles[0].x;
    const dy = handles[1].y - handles[0].y;
    if (Math.abs(dx) > Math.abs(dy) * 1.2) return "horizontal";
    if (Math.abs(dx) > Math.abs(dy) * 0.6) return dy >= 0 ? "diagonal-down" : "diagonal-up";
  }

  return "vertical";
}

function gradientStopToColor(stop, paintOpacity) {
  const opacity = typeof paintOpacity === "number" ? paintOpacity : 1;
  return {
    r: stop.color.r,
    g: stop.color.g,
    b: stop.color.b,
    a: stop.color.a * opacity,
  };
}

function getSolidStrokeColor(node) {
  if (!node.strokes || node.strokes === figma.mixed || node.strokes.length === 0) {
    return null;
  }

  const stroke = node.strokes.find((item) => item.type === "SOLID" && item.visible !== false);
  if (!stroke) {
    return null;
  }

  return {
    r: stroke.color.r,
    g: stroke.color.g,
    b: stroke.color.b,
    a: typeof stroke.opacity === "number" ? stroke.opacity : 1,
  };
}

function getStrokeWeight(node) {
  const value = node.strokeWeight;
  return typeof value === "number" && Number.isFinite(value) ? value : 0;
}

function getDropShadow(node) {
  if (!node.effects || node.effects === figma.mixed || node.effects.length === 0) {
    return null;
  }

  const effect = node.effects.find((item) => item.type === "DROP_SHADOW" && item.visible !== false);
  if (!effect) {
    return null;
  }

  return {
    color: {
      r: effect.color.r,
      g: effect.color.g,
      b: effect.color.b,
      a: effect.color.a,
    },
    offsetX: normalizeNumber(effect.offset && effect.offset.x, 0),
    offsetY: normalizeNumber(effect.offset && effect.offset.y, 0),
    blur: normalizeNumber(effect.radius, 0),
  };
}

function shouldEnableWordWrap(node) {
  // Figma auto-width text should stay single-line in Unity. Fixed-width text boxes
  // are the only case where TMP word wrapping should be enabled.
  if (node.characters && String(node.characters).includes("\n")) {
    return true;
  }

  return node.textAutoResize === "HEIGHT";
}

function getFontName(node) {
  if (!node.fontName || node.fontName === figma.mixed) {
    return "";
  }

  const family = node.fontName.family || "";
  const style = node.fontName.style || "";
  return `${family} ${style}`.trim();
}

function hasVisibleChildren(node) {
  return hasExportableChildren(node, false);
}

function hasExportableChildren(node, includeHidden) {
  return "children" in node && node.children.some((child) => (includeHidden || child.visible !== false || isStateLayerName(child.name)) && getBounds(child));
}

function isStateLayerName(name) {
  return hasLayerPrefix(name, "state");
}

function hasLayerPrefix(name, prefix) {
  const normalized = String(name || "").trim().toLowerCase();
  return normalized === prefix ||
    normalized.startsWith(`${prefix}_`) ||
    normalized.startsWith(`${prefix}-`) ||
    normalized.startsWith(`${prefix}/`);
}

function inspectStructure() {
  const selection = figma.currentPage.selection;
  if (selection.length === 0) {
    figma.ui.postMessage({ type: "error", message: "请先选中一个 Frame、组件或图层。" });
    return;
  }

  const nodes = [];
  for (const selected of selection) {
    collectStructureNodes(selected, nodes);
  }

  const menus = nodes.filter((node) => hasLayerPrefix(node.name, "menu"));
  const tabs = nodes.filter((node) => hasLayerPrefix(node.name, "tab"));
  const states = nodes.filter((node) => hasLayerPrefix(node.name, "state"));
  const buttons = nodes.filter((node) => hasLayerPrefix(node.name, "btn"));
  const hiddenStates = states.filter((node) => node.visible === false);
  const warnings = [];
  const menuDetails = [];

  for (const menu of menus) {
    const menuTabs = getDirectChildren(menu).filter((child) => hasLayerPrefix(child.name, "tab"));
    if (menuTabs.length === 0) {
      warnings.push(`${menu.name} 下面没有 tab_ 子项`);
    }

    menuDetails.push({
      name: menu.name,
      tabCount: menuTabs.length,
    });
  }

  for (const tab of tabs) {
    const children = getDirectChildren(tab);
    const normal = children.find((child) => stateKey(child.name) === "normal");
    const selected = children.find((child) => stateKey(child.name) === "selected");
    if (!normal) {
      warnings.push(`${tab.name} 缺少 state_normal`);
    }
    if (!selected) {
      warnings.push(`${tab.name} 缺少 state_selected`);
    }

    const parent = findParentInSelection(selection, tab);
    if (parent && !hasLayerPrefix(parent.name, "menu")) {
      warnings.push(`${tab.name} 不在 menu_ 下面，会按普通按钮导出`);
    }
  }

  figma.ui.postMessage({
    type: "structure-report",
    summary: {
      menuCount: menus.length,
      tabCount: tabs.length,
      stateCount: states.length,
      hiddenStateCount: hiddenStates.length,
      buttonCount: buttons.length,
      warningCount: warnings.length,
    },
    warnings,
    menus: menuDetails,
  });
}

function renameSelectedLayers(kind) {
  const selection = figma.currentPage.selection;
  if (selection.length === 0) {
    figma.ui.postMessage({ type: "error", message: "请先选中要命名的图层。" });
    return;
  }

  let changed = 0;
  for (const node of selection) {
    const base = layerKeyFromName(node.name);
    if (kind === "menu") {
      node.name = `menu_${base}`;
    } else if (kind === "tab") {
      node.name = `tab_${base}`;
    } else if (kind === "btn") {
      node.name = `btn_${base}`;
    } else if (kind === "state-normal") {
      node.name = "state_normal";
    } else if (kind === "state-selected") {
      node.name = "state_selected";
    } else {
      continue;
    }
    changed++;
  }

  figma.notify(`已重命名 ${changed} 个图层`);
  figma.ui.postMessage({
    type: "rename-done",
    message: `已重命名 ${changed} 个图层。`,
  });
}

async function createMenuTemplate() {
  await figma.loadFontAsync({ family: "Inter", style: "Regular" });
  await figma.loadFontAsync({ family: "Inter", style: "Bold" });

  const labels = ["home", "shop", "pet", "rank", "event"];
  const frame = figma.createFrame();
  frame.name = "menu_main";
  frame.resize(360, 82);
  frame.fills = [{ type: "SOLID", color: { r: 0.18, g: 0.14, b: 0.31 } }];
  frame.x = figma.viewport.center.x - frame.width / 2;
  frame.y = figma.viewport.center.y - frame.height / 2;

  const tabWidth = frame.width / labels.length;
  labels.forEach((key, index) => {
    const tab = figma.createFrame();
    tab.name = `tab_${key}`;
    tab.resize(tabWidth, 82);
    tab.x = index * tabWidth;
    tab.y = 0;
    tab.fills = [];
    frame.appendChild(tab);

    const normal = createTemplateState("state_normal", tabWidth, 82, false, key, index !== 0);
    const selected = createTemplateState("state_selected", tabWidth, 82, true, key, index === 0);
    tab.appendChild(normal);
    tab.appendChild(selected);
  });

  figma.currentPage.appendChild(frame);
  figma.currentPage.selection = [frame];
  figma.viewport.scrollAndZoomIntoView([frame]);
  figma.notify("已生成 menu_main 模板");
  figma.ui.postMessage({
    type: "rename-done",
    message: "已生成标准 menu_/tab_/state_ 模板。",
  });
}

function createTemplateState(name, width, height, selected, key, visible) {
  const state = figma.createFrame();
  state.name = name;
  state.resize(width, height);
  state.visible = visible;
  state.fills = [];

  const bg = figma.createRectangle();
  bg.name = selected ? "selected_bg" : "normal_bg";
  bg.resize(width - 8, height - 10);
  bg.x = 4;
  bg.y = selected ? 4 : 10;
  bg.cornerRadius = selected ? 14 : 8;
  bg.fills = [{
    type: "SOLID",
    color: selected ? { r: 1, g: 0.78, b: 0.16 } : { r: 0.46, g: 0.29, b: 0.9 },
  }];
  state.appendChild(bg);

  const icon = figma.createEllipse();
  icon.name = "icon";
  icon.resize(28, 28);
  icon.x = (width - 28) / 2;
  icon.y = selected ? 14 : 18;
  icon.fills = [{
    type: "SOLID",
    color: selected ? { r: 1, g: 0.55, b: 0.05 } : { r: 1, g: 0.72, b: 0.12 },
  }];
  state.appendChild(icon);

  const label = figma.createText();
  label.name = "label";
  label.fontName = { family: "Inter", style: selected ? "Bold" : "Regular" };
  label.characters = key[0].toUpperCase() + key.slice(1);
  label.fontSize = 13;
  label.textAlignHorizontal = "CENTER";
  label.resize(width, 20);
  label.x = 0;
  label.y = selected ? 48 : 52;
  label.fills = [{ type: "SOLID", color: { r: 1, g: 1, b: 1 } }];
  state.appendChild(label);

  return state;
}

function collectStructureNodes(node, nodes) {
  nodes.push(node);
  if (!("children" in node)) return;
  for (const child of node.children) {
    collectStructureNodes(child, nodes);
  }
}

function getDirectChildren(node) {
  return "children" in node ? Array.from(node.children) : [];
}

function findParentInSelection(selection, target) {
  for (const root of selection) {
    const parent = findParentRecursive(root, target);
    if (parent) return parent;
  }
  return null;
}

function findParentRecursive(parent, target) {
  if (!("children" in parent)) return null;
  for (const child of parent.children) {
    if (child.id === target.id) return parent;
    const found = findParentRecursive(child, target);
    if (found) return found;
  }
  return null;
}

function stateKey(name) {
  return hasLayerPrefix(name, "state") ? keyFromPrefixedName(name, "state") : "";
}

function layerKeyFromName(name) {
  let value = String(name || "item").trim();
  for (const prefix of ["menu", "tab", "state", "btn"]) {
    if (hasLayerPrefix(value, prefix)) {
      value = keyFromPrefixedName(value, prefix);
      break;
    }
  }

  return sanitizeLayerKey(value);
}

function keyFromPrefixedName(name, prefix) {
  const value = String(name || "").trim();
  for (const marker of [`${prefix}_`, `${prefix}-`, `${prefix}/`]) {
    if (value.toLowerCase().startsWith(marker)) {
      return sanitizeLayerKey(value.slice(marker.length));
    }
  }
  return sanitizeLayerKey(value);
}

function sanitizeLayerKey(value) {
  const text = String(value || "item").trim().toLowerCase();
  return text.replace(/[^0-9a-z\u4e00-\u9fa5]+/g, "_").replace(/^_+|_+$/g, "") || "item";
}

function getBounds(node) {
  if (node.absoluteBoundingBox) {
    return {
      x: node.absoluteBoundingBox.x,
      y: node.absoluteBoundingBox.y,
      width: node.absoluteBoundingBox.width,
      height: node.absoluteBoundingBox.height,
    };
  }

  if ("absoluteTransform" in node && "width" in node && "height" in node) {
    return {
      x: node.absoluteTransform[0][2],
      y: node.absoluteTransform[1][2],
      width: node.width,
      height: node.height,
    };
  }

  return null;
}

function unionBounds(boundsList) {
  if (boundsList.length === 0) return null;
  const minX = Math.min(...boundsList.map((bounds) => bounds.x));
  const minY = Math.min(...boundsList.map((bounds) => bounds.y));
  const maxX = Math.max(...boundsList.map((bounds) => bounds.x + bounds.width));
  const maxY = Math.max(...boundsList.map((bounds) => bounds.y + bounds.height));
  return { x: minX, y: minY, width: maxX - minX, height: maxY - minY };
}

function getDefaultRootName(selection) {
  return selection.length === 1 ? selection[0].name : "FigmaSelection";
}

function stableNodeId(node) {
  return String(node.id).replace(/[^0-9a-zA-Z_-]/g, "_");
}

function getHealthUrl(bridgeUrl) {
  return bridgeUrl.replace(/\/export$/, "/health");
}

function getPrefabUrl(bridgeUrl) {
  return bridgeUrl.replace(/\/export$/, "/prefab");
}

async function waitForBridge(bridgeUrl, startCommand, startupCommand) {
  const deadline = Date.now() + 3000;
  let lastError = "";

  while (Date.now() < deadline) {
    try {
      const response = await fetch(getHealthUrl(bridgeUrl));
      if (response.ok) {
        const data = await response.json();
        figma.ui.postMessage({
          type: "bridge-health",
          message: `Bridge 已启动。\n工程：${data.projectRoot}\n监听：${data.prefix}`,
        });
        return;
      }

      lastError = `${response.status} ${await response.text()}`;
    } catch (error) {
      lastError = error && error.message ? error.message : String(error);
    }

    await delay(800);
  }

  figma.ui.postMessage({
    type: "error",
    message: `没有检测到 Bridge。\n\n已把启动命令复制到剪贴板，请粘贴到 PowerShell 运行：\n${startCommand}\n\n推荐只做一次 Windows 登录自启：\n${startupCommand}\n\n最后一次错误：${lastError}`,
  });
}

function delay(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

function quoteArg(value) {
  return `"${String(value).replace(/"/g, '\\"')}"`;
}

function sanitizeName(value) {
  const text = String(value || "FigmaPrefab").trim();
  return text.replace(/[\\/:*?"<>|\[\]]/g, "_") || "FigmaPrefab";
}

function uint8ToBase64(bytes) {
  let binary = "";
  const chunkSize = 0x8000;
  for (let i = 0; i < bytes.length; i += chunkSize) {
    const chunk = bytes.subarray(i, i + chunkSize);
    binary += String.fromCharCode.apply(null, chunk);
  }

  return btoa(binary);
}
