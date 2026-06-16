let stage = document.getElementById('stage');
let content = document.getElementById('content');
let playerMarker = document.getElementById('playerMarker');
let markerLayer = document.getElementById('markerLayer');
let hazardOutlineLayer = document.getElementById('hazardOutlineLayer');
let svg = content.querySelector('svg');

let scale = 1;
let panX = 0;
let panY = 0;
let isPanning = false;
let lastX = 0;
let lastY = 0;
let playerMarkerData = null;

function getViewBox() {
    let raw = svg.getAttribute('viewBox');
    if (!raw) return { x: 0, y: 0, w: 1000, h: 1000 };

    let vb = raw.trim().split(/\s+/).map(Number);
    return { x: vb[0], y: vb[1], w: vb[2], h: vb[3] };
}

function initialize() {
    let vb = getViewBox();

    svg.setAttribute('width', vb.w);
    svg.setAttribute('height', vb.h);

    content.style.width = vb.w + 'px';
    content.style.height = vb.h + 'px';

    if (hazardOutlineLayer) {
        hazardOutlineLayer.setAttribute('width', vb.w);
        hazardOutlineLayer.setAttribute('height', vb.h);
        hazardOutlineLayer.setAttribute('viewBox', `0 0 ${vb.w} ${vb.h}`);
    }

    resetView();
}

function applyTransform() {
    content.style.transform = `translate(${panX}px, ${panY}px) scale(${scale})`;
    updatePlayerMarkerVisual();
    updateMapMarkerVisuals();
}

function resetView() {
    let vb = getViewBox();

    let sx = stage.clientWidth / vb.w;
    let sy = stage.clientHeight / vb.h;

    scale = Math.min(sx, sy) * 0.95;

    panX = (stage.clientWidth - vb.w * scale) / 2;
    panY = (stage.clientHeight - vb.h * scale) / 2;

    applyTransform();
}

function setPlayerMarkerNormalized(nx, ny, directionDegrees) {
    let vb = getViewBox();

    playerMarkerData = {
        x: nx * vb.w,
        y: ny * vb.h,
        direction: directionDegrees
    };

    updatePlayerMarkerVisual();
}

function updatePlayerMarkerVisual() {
    if (!playerMarkerData) return;

    let inverseScale = 1 / scale;

    playerMarker.style.left = playerMarkerData.x + 'px';
    playerMarker.style.top = playerMarkerData.y + 'px';
    playerMarker.style.display = 'block';

    playerMarker.style.transform =
        `rotate(${playerMarkerData.direction + 180}deg) scale(${inverseScale})`;
}

function clearMapMarkers() {
    markerLayer.innerHTML = '';
    if (hazardOutlineLayer) {
        hazardOutlineLayer.innerHTML = '';
    }
}

function addMapMarkers(markers) {
    clearMapMarkers();

    let vb = getViewBox();

    for (let m of markers) {
        let marker = document.createElement('div');

        marker.className = 'mapMarker ' + m.cssClass;
        marker.dataset.markerType = m.markerType;
        marker.dataset.faction = m.faction || '';
        marker.dataset.questCategory = m.questCategory || '';

        marker.title = m.tooltip;

        let x = m.normalizedX * vb.w;
        let y = m.normalizedY * vb.h;

        marker.style.left = x + 'px';
        marker.style.top = y + 'px';

        let dot = document.createElement('div');
        dot.className = 'markerDot';

        if (m.markerType === 'quest' && m.questCategory === 'item' && m.questItemIconLink) {
            dot.classList.add('markerItemIcon');
            dot.style.backgroundImage = "url('" + m.questItemIconLink.replace(/'/g, '%27') + "')";
        }

        marker.appendChild(dot);

        if (
            m.markerType !== 'spawn-pmc' &&
            m.markerType !== 'spawn-scav' &&
            m.markerType !== 'spawn-boss' &&
            !m.hideLabel
        ) {
            let label = document.createElement('div');
            label.className = 'markerLabel';
            label.textContent = m.name;

            if (m.markerType === 'label') {
                label.style.fontSize = (m.labelSize || 14) + 'px';
                label.style.transform = 'translate(-50%, -50%) rotate(' + (m.labelRotation || 0) + 'deg)';
            }

            marker.appendChild(label);
        }

        marker.addEventListener('click', function(e) {
            e.stopPropagation();

            if (window.chrome && window.chrome.webview) {
                window.chrome.webview.postMessage({
                    messageType: 'markerClicked',
                    name: m.name,
                    markerType: m.markerType,
                    faction: m.faction || '',
                    zoneName: m.zoneName || '',
                    categories: m.categories || '',
                    conditions: m.conditions || '',
                    position: m.position || ''
                });
            }
        });

        markerLayer.appendChild(marker);
    }

    updateMapMarkerVisuals();
}

function updateMapMarkerVisuals() {
    let inverseScale = 1 / scale;

    document.querySelectorAll('.mapMarker').forEach(function(marker) {
        marker.style.transform = `scale(${inverseScale})`;
    });
}

function setExtractFactionVisibility(faction, visible) {
    document.querySelectorAll('.mapMarker[data-marker-type="extract"][data-faction="' + faction + '"]').forEach(function(marker) {
        marker.style.display = visible ? 'block' : 'none';
    });
}

function setTransitVisibility(visible) {
    document.querySelectorAll('.mapMarker[data-marker-type="transit"]').forEach(function(marker) {
        marker.style.display = visible ? 'block' : 'none';
    });
}

function setSpawnVisibility(spawnType, visible) {
    document.querySelectorAll('.mapMarker[data-marker-type="' + spawnType + '"]').forEach(function(marker) {
        marker.style.display = visible ? 'block' : 'none';
    });
}

function setLabelVisibility(visible) {
    document.querySelectorAll('.mapMarker[data-marker-type="label"]').forEach(function(marker) {
        marker.style.display = visible ? 'block' : 'none';
    });
}

function setQuestVisibility(visible) {
    document.querySelectorAll('.mapMarker[data-marker-type="quest"]').forEach(function(marker) {
        marker.style.display = visible ? 'block' : 'none';
    });
}

function setQuestCategoryVisibility(category, visible) {
    document.querySelectorAll('.mapMarker[data-marker-type="quest"][data-quest-category="' + category + '"]').forEach(function(marker) {
        marker.style.display = visible ? 'block' : 'none';
    });
}

function setHazardVisibility(visible) {
    document.querySelectorAll('.mapMarker[data-marker-type="hazard"]').forEach(function(marker) {
        marker.style.display = visible ? 'block' : 'none';
    });

    if (hazardOutlineLayer) {
        hazardOutlineLayer.querySelectorAll('polygon[data-marker-type="hazard"]').forEach(function(polygon) {
            polygon.style.display = visible ? '' : 'none';
        });
    }
}

function setSwitchVisibility(visible) {
    document.querySelectorAll('.mapMarker[data-marker-type="switch"]').forEach(function(marker) {
        marker.style.display = visible ? 'block' : 'none';
    });
}

function setBtrStopVisibility(visible) {
    document.querySelectorAll('.mapMarker[data-marker-type="btr-stop"]').forEach(function(marker) {
        marker.style.display = visible ? 'block' : 'none';
    });
}

function applyMarkerFilters(filters) {
    if (!filters) return;

    setExtractFactionVisibility('pmc', !!filters.pmcExtracts);
    setExtractFactionVisibility('scav', !!filters.scavExtracts);
    setExtractFactionVisibility('shared', !!filters.sharedExtracts);
    setTransitVisibility(!!filters.transits);
    setSpawnVisibility('spawn-pmc', !!filters.pmcSpawns);
    setSpawnVisibility('spawn-scav', !!filters.scavSpawns);
    setSpawnVisibility('spawn-boss', !!filters.bossSpawns);
    setLabelVisibility(!!filters.labels);
    setQuestCategoryVisibility('item', !!filters.questItems);
    setQuestCategoryVisibility('objective', !!filters.questObjectives);
    setHazardVisibility(!!filters.hazards);
    setSwitchVisibility(!!filters.switches);
    setBtrStopVisibility(!!filters.btrStops);
}

let mapLevelState = {
    defaultLayerId: null,
    overlayLayerIds: [],
    showBaseLayer: true,
    dimBase: false,
    activeLevelIds: []
};

function setupMapLayerClasses(defaultLayerId, overlayLayerIds) {
    const svgRoot = document.querySelector('#content svg');
    if (!svgRoot || !defaultLayerId) return;

    for (const child of svgRoot.children) {
        if (child.tagName !== 'g' || !child.id) continue;

        child.classList.remove('base-layer', 'overlay-layer', 'hidden-layer', 'force-hidden');

        const keepWithBase = child.dataset && child.dataset.keepWithGroup === defaultLayerId;
        if (child.id === defaultLayerId || keepWithBase) {
            child.classList.add('base-layer');
        } else {
            child.classList.add('overlay-layer', 'hidden-layer');
        }
    }
}

function refreshMapLevelDisplay() {
    const svgRoot = document.querySelector('#content svg');
    if (!svgRoot || !mapLevelState.defaultLayerId) return;

    const activeSet = new Set(mapLevelState.activeLevelIds || []);
    const hasActiveOverlay = activeSet.size > 0;

    if (mapLevelState.showBaseLayer && mapLevelState.dimBase && hasActiveOverlay) {
        svgRoot.classList.add('off-level');
    } else {
        svgRoot.classList.remove('off-level');
    }

    for (const child of svgRoot.children) {
        if (child.tagName !== 'g' || !child.id) continue;

        if (child.classList.contains('base-layer')) {
            if (!mapLevelState.showBaseLayer) {
                child.classList.add('force-hidden');
            } else {
                child.classList.remove('force-hidden');
            }
            continue;
        }

        if (child.classList.contains('overlay-layer')) {
            if (activeSet.has(child.id)) {
                child.classList.remove('hidden-layer');
            } else {
                child.classList.add('hidden-layer');
            }
        }
    }
}

function applyMapLevelState(state) {
    if (!state || !state.defaultLayerId) return;

    const overlayLayerIds = state.overlayLayerIds || [];
    const needsSetup =
        mapLevelState.defaultLayerId !== state.defaultLayerId ||
        JSON.stringify(mapLevelState.overlayLayerIds) !== JSON.stringify(overlayLayerIds);

    if (needsSetup) {
        setupMapLayerClasses(state.defaultLayerId, overlayLayerIds);
    }

    mapLevelState.defaultLayerId = state.defaultLayerId;
    mapLevelState.overlayLayerIds = overlayLayerIds;
    mapLevelState.showBaseLayer = state.showBaseLayer !== false;
    mapLevelState.dimBase = state.dimBase === true;
    mapLevelState.activeLevelIds = state.activeLevelIds || [];

    refreshMapLevelDisplay();
}

stage.addEventListener('wheel', function(e) {
    e.preventDefault();

    let oldScale = scale;
    let zoomFactor = e.deltaY < 0 ? 1.15 : 1 / 1.15;
    let newScale = Math.max(0.2, Math.min(oldScale * zoomFactor, 12));

    let rect = stage.getBoundingClientRect();
    let mx = e.clientX - rect.left;
    let my = e.clientY - rect.top;

    let ratio = newScale / oldScale;

    panX = mx - (mx - panX) * ratio;
    panY = my - (my - panY) * ratio;

    scale = newScale;
    applyTransform();
});

stage.addEventListener('mousedown', function(e) {
    isPanning = true;
    lastX = e.clientX;
    lastY = e.clientY;
    stage.style.cursor = 'grabbing';
});

window.addEventListener('mouseup', function() {
    isPanning = false;
    stage.style.cursor = 'grab';
});

window.addEventListener('mousemove', function(e) {
    if (!isPanning) return;

    panX += e.clientX - lastX;
    panY += e.clientY - lastY;

    lastX = e.clientX;
    lastY = e.clientY;

    applyTransform();
});

window.addEventListener('resize', resetView);

initialize();
