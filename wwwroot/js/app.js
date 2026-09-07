let db = { activeProfileId: '', profiles: [] };
let currentTargetId = '';
let progressChartInstance = null;
let velocityChartInstance = null;

// Initialize Date Input
const dateInput = document.getElementById('date');
if (dateInput) {
  dateInput.value = new Date().toISOString().split('T')[0];
}

// ----------------- THEME MANAGEMENT -----------------
function initTheme() {
  const savedTheme = localStorage.getItem('roadmap_theme') || 'dark';
  document.documentElement.setAttribute('data-theme', savedTheme);
  const themeSelect = document.getElementById('themeSelect');
  if (themeSelect) themeSelect.value = savedTheme;
}

function setAppTheme(themeName) {
  document.documentElement.setAttribute('data-theme', themeName);
  localStorage.setItem('roadmap_theme', themeName);
  const profile = getActiveProfile();
  if (profile && currentTargetId) {
    renderTargetContent(profile);
  }
}

function getCssVar(name) {
  return getComputedStyle(document.documentElement).getPropertyValue(name).trim();
}

// ----------------- DATA LOADING -----------------
async function loadData() {
  try {
    const res = await fetch('/api/database');
    if (!res.ok) throw new Error("Could not fetch database");
    db = await res.json();
    renderProfileSelector();
    renderActiveProfile();
  } catch (err) {
    console.error("Data load failed:", err);
  }
}

function getActiveProfile() {
  return db.profiles.find(p => p.id === db.activeProfileId) || db.profiles[0];
}

function renderProfileSelector() {
  const select = document.getElementById('profileSelect');
  if (!select) return;
  select.innerHTML = db.profiles.map(p => `
    <option value="${p.id}" ${p.id === db.activeProfileId ? 'selected' : ''}>👤 ${escapeHtml(p.username)}</option>
  `).join('');
}

function renderActiveProfile() {
  const profile = getActiveProfile();
  if (!profile) return;

  const roleElem = document.getElementById('displayRole');
  if (roleElem) roleElem.innerText = profile.roleTitle || 'Developer';

  if (!profile.targets || profile.targets.length === 0) {
    currentTargetId = '';
  } else if (!currentTargetId || !profile.targets.some(t => t.id === currentTargetId)) {
    currentTargetId = profile.targets[0].id;
  }

  renderTargetTabs(profile);
  renderTargetContent(profile);
}

function renderTargetTabs(profile) {
  const container = document.getElementById('targetTabs');
  const deleteBtn = document.getElementById('deleteTargetBtn');
  if (!container) return;

  if (!profile.targets || profile.targets.length === 0) {
    container.innerHTML = `<span style="color:var(--text-muted); font-size:0.82rem; padding: 4px 6px;">No active targets. Click '+ New Target' to create one.</span>`;
    if (deleteBtn) deleteBtn.style.display = 'none';
    return;
  }

  if (deleteBtn) deleteBtn.style.display = 'inline-flex';
  container.innerHTML = profile.targets.map(t => `
    <div class="target-pill ${t.id === currentTargetId ? 'active' : ''}" onclick="switchTarget('${t.id}')">
      🎯 ${escapeHtml(t.title)} (${t.targetDays}d)
    </div>
  `).join('');
}

function switchTarget(targetId) {
  currentTargetId = targetId;
  const profile = getActiveProfile();
  renderTargetTabs(profile);
  renderTargetContent(profile);
  resetForm();
}

// ----------------- DASHBOARD & CHARTS -----------------
function resetDashboard() {
  document.getElementById('stat-completed').innerText = "0 / 0";
  document.getElementById('stat-progress').innerText = "0%";
  document.getElementById('stat-hours').innerText = "0h";
  document.getElementById('stat-commits').innerText = "0";
  document.getElementById('tableHeading').innerText = "No Target Selected";
  document.getElementById('logsTbody').innerHTML = `<tr><td colspan="7" style="text-align:center; color: var(--text-muted); padding:20px;">No targets available. Create a new target above to begin tracking.</td></tr>`;
  resetCharts();
}

function resetCharts() {
  if (progressChartInstance) {
    progressChartInstance.destroy();
    progressChartInstance = null;
  }
  if (velocityChartInstance) {
    velocityChartInstance.destroy();
    velocityChartInstance = null;
  }
}

function renderTargetContent(profile) {
  const target = profile.targets?.find(t => t.id === currentTargetId);
  if (!target) {
    resetDashboard();
    return;
  }

  document.getElementById('tableHeading').innerText = `${target.title} — ${target.description || ''}`;

  const logs = (target.logs || []).sort((a, b) => a.dayNumber - b.dayNumber);
  const completed = logs.length;
  const pct = Math.min(100, Math.round((completed / target.targetDays) * 100));
  const totalMin = logs.reduce((acc, c) => acc + (c.theoryMinutes || 0) + (c.labMinutes || 0), 0);
  const commits = logs.filter(l => l.gitPushed).length;

  document.getElementById('stat-completed').innerText = `${completed} / ${target.targetDays}`;
  document.getElementById('stat-progress').innerText = `${pct}%`;
  document.getElementById('stat-hours').innerText = `${(totalMin / 60).toFixed(1)}h`;
  document.getElementById('stat-commits').innerText = commits;

  const tbody = document.getElementById('logsTbody');
  if (logs.length === 0) {
    tbody.innerHTML = `<tr><td colspan="7" style="text-align: center; color: var(--text-muted); padding: 16px;">No entries logged yet for this target.</td></tr>`;
  } else {
    tbody.innerHTML = logs.map(l => `
      <tr>
        <td><strong style="color: var(--accent);">#${l.dayNumber}</strong></td>
        <td style="color: var(--text-muted);">${l.date}</td>
        <td><strong>${escapeHtml(l.topic)}</strong></td>
        <td>${l.notes ? `<div class="notes-box">${escapeHtml(l.notes)}</div>` : `<span style="color:var(--text-muted); font-style:italic;">No notes</span>`}</td>
        <td>${(l.theoryMinutes || 0) + (l.labMinutes || 0)}m</td>
        <td><span class="badge ${l.gitPushed ? 'pushed' : 'pending'}">${l.gitPushed ? '✓ Pushed' : 'Pending'}</span></td>
        <td>
          <button class="btn-ui btn-glass" style="padding: 2px 7px; font-size: 0.72rem;" onclick="loadForEdit(${l.dayNumber})">Edit</button>
          <button class="btn-ui btn-danger-ghost" style="padding: 2px 7px; font-size: 0.72rem;" onclick="deleteLog(${l.dayNumber})">Del</button>
        </td>
      </tr>
    `).join('');
  }

  renderCharts(completed, target.targetDays, logs);
}

function renderCharts(completed, targetDays, logs) {
  if (typeof Chart === 'undefined') return;

  const accentColor = getCssVar('--accent') || '#38bdf8';
  const chartEmpty = getCssVar('--chart-empty') || '#1a2742';
  const textColor = getCssVar('--text-muted') || '#8da2c0';
  const borderColor = getCssVar('--border') || '#1f2e4d';

  // 1. Doughnut Chart
  const remaining = Math.max(0, targetDays - completed);
  const ctxDoughnut = document.getElementById('progressChart').getContext('2d');
  if (progressChartInstance) progressChartInstance.destroy();
  progressChartInstance = new Chart(ctxDoughnut, {
    type: 'doughnut',
    data: {
      labels: ['Done', 'Left'],
      datasets: [{ data: [completed, remaining], backgroundColor: [accentColor, chartEmpty], borderWidth: 0 }]
    },
    options: { responsive: true, maintainAspectRatio: false, plugins: { legend: { display: false } }, cutout: '74%' }
  });

  // 2. Velocity Bar Chart
  const recent = logs.slice(-7);
  const ctxBar = document.getElementById('velocityChart').getContext('2d');
  if (velocityChartInstance) velocityChartInstance.destroy();
  velocityChartInstance = new Chart(ctxBar, {
    type: 'bar',
    data: {
      labels: recent.map(r => `D${r.dayNumber}`),
      datasets: [{ data: recent.map(r => (r.theoryMinutes || 0) + (r.labMinutes || 0)), backgroundColor: '#10b981', borderRadius: 4 }]
    },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      scales: {
        x: { grid: { display: false }, ticks: { color: textColor, font: { size: 9 } } },
        y: { grid: { color: borderColor }, ticks: { color: textColor, font: { size: 9 } } }
      },
      plugins: { legend: { display: false } }
    }
  });
}

// ----------------- PROFILE & TARGET CRUD -----------------
async function switchProfile(profileId) {
  await fetch(`/api/profiles/${profileId}/activate`, { method: 'POST' });
  db.activeProfileId = profileId;
  currentTargetId = '';
  loadData();
}

async function promptNewProfile() {
  const username = prompt("Enter Username / Profile Name:");
  if (!username) return;
  const role = prompt("Enter Role / Track Subtitle:", "Platform & .NET Transition");

  await fetch('/api/profiles', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ username, roleTitle: role })
  });
  currentTargetId = '';
  loadData();
}

async function deleteCurrentProfile() {
  if (db.profiles.length <= 1) {
    alert("At least one profile must be retained.");
    return;
  }
  if (!confirm(`Delete profile '${getActiveProfile().username}' and all its targets?`)) return;

  const res = await fetch(`/api/profiles/${db.activeProfileId}`, { method: 'DELETE' });
  if (res.ok) {
    currentTargetId = '';
    loadData();
  }
}

async function promptNewTarget() {
  const profile = getActiveProfile();
  const title = prompt("Target Title (e.g., '90-Day DevOps Mastery'):");
  if (!title) return;
  const days = parseInt(prompt("Target Total Days (e.g. 30, 60, 90):", "30"));
  if (isNaN(days) || days <= 0) return;
  const desc = prompt("Target Description:", "");

  const res = await fetch(`/api/profiles/${profile.id}/targets`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ title, targetDays: days, description: desc || "" })
  });
  if (res.ok) {
    const created = await res.json();
    currentTargetId = created.id;
    loadData();
  }
}

async function deleteCurrentTarget() {
  const profile = getActiveProfile();
  if (!currentTargetId) {
    alert("No target selected to delete.");
    return;
  }
  if (!confirm("Delete this target and all its logs?")) return;

  const res = await fetch(`/api/profiles/${profile.id}/targets/${currentTargetId}`, { method: 'DELETE' });
  if (res.ok) {
    currentTargetId = '';
    resetForm();
    loadData();
  } else {
    alert("Failed to delete target.");
  }
}

// ----------------- LOG CRUD -----------------
const logForm = document.getElementById('logForm');
if (logForm) {
  logForm.addEventListener('submit', async (e) => {
    e.preventDefault();
    const profile = getActiveProfile();
    if (!currentTargetId) {
      alert("Please select or create a target first.");
      return;
    }

    const payload = {
      dayNumber: parseInt(document.getElementById('dayNumber').value),
      date: document.getElementById('date').value,
      topic: document.getElementById('topic').value,
      theoryMinutes: parseInt(document.getElementById('theoryMinutes').value) || 0,
      labMinutes: parseInt(document.getElementById('labMinutes').value) || 0,
      gitPushed: document.getElementById('gitPushed').checked,
      notes: document.getElementById('notes').value
    };

    await fetch(`/api/profiles/${profile.id}/targets/${currentTargetId}/logs`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload)
    });
    resetForm();
    loadData();
  });
}

function loadForEdit(dayNumber) {
  const profile = getActiveProfile();
  const target = profile.targets?.find(t => t.id === currentTargetId);
  const log = target?.logs.find(l => l.dayNumber === dayNumber);
  if (!log) return;

  document.getElementById('dayNumber').value = log.dayNumber;
  document.getElementById('date').value = log.date;
  document.getElementById('topic').value = log.topic;
  document.getElementById('theoryMinutes').value = log.theoryMinutes;
  document.getElementById('labMinutes').value = log.labMinutes;
  document.getElementById('gitPushed').checked = log.gitPushed;
  document.getElementById('notes').value = log.notes || '';
  document.getElementById('form-title').innerText = `Edit Day #${log.dayNumber}`;
  document.getElementById('submitBtn').innerText = "Update Session";
}

function resetForm() {
  const form = document.getElementById('logForm');
  if (form) form.reset();
  const dateElem = document.getElementById('date');
  if (dateElem) dateElem.value = new Date().toISOString().split('T')[0];
  document.getElementById('theoryMinutes').value = 30;
  document.getElementById('labMinutes').value = 60;
  document.getElementById('gitPushed').checked = true;
  document.getElementById('form-title').innerText = "Log Today's Session";
  document.getElementById('submitBtn').innerText = "Save Session";
}

async function deleteLog(dayNumber) {
  if (!confirm(`Delete Day #${dayNumber}?`)) return;
  const profile = getActiveProfile();
  await fetch(`/api/profiles/${profile.id}/targets/${currentTargetId}/logs/${dayNumber}`, { method: 'DELETE' });
  loadData();
}

// ----------------- BACKUP & RESTORE -----------------
async function downloadBackup() {
  try {
    const response = await fetch('/api/backup/export');
    if (!response.ok) throw new Error("Failed to export backup");

    const blob = await response.blob();
    const url = window.URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `roadmap-backup-${new Date().toISOString().slice(0, 10)}.json`;
    document.body.appendChild(a);
    a.click();
    a.remove();
    window.URL.revokeObjectURL(url);
  } catch (err) {
    alert("Failed to export backup: " + err.message);
  }
}

async function handleRestoreFile(event) {
  const file = event.target.files[0];
  if (!file) return;

  const confirmRestore = confirm(
    `Are you sure you want to restore data from "${file.name}"?\nThis will overwrite current users, targets, and logs.`
  );
  if (!confirmRestore) {
    event.target.value = '';
    return;
  }

  const reader = new FileReader();
  reader.onload = async (e) => {
    try {
      const parsedData = JSON.parse(e.target.result);
      const profilesList = parsedData.profiles || parsedData.Profiles;

      if (!profilesList || !Array.isArray(profilesList)) {
        throw new Error("Invalid backup schema: 'profiles' array is missing.");
      }

      const normalizedPayload = {
        activeProfileId: parsedData.activeProfileId || parsedData.ActiveProfileId || (profilesList[0] ? (profilesList[0].id || profilesList[0].Id) : ""),
        profiles: profilesList
      };

      const res = await fetch('/api/backup/restore', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(normalizedPayload)
      });

      if (res.ok) {
        alert("Database restored successfully!");
        currentTargetId = '';
        loadData();
      } else {
        const err = await res.text();
        alert("Restore failed: " + err);
      }
    } catch (err) {
      alert("Error parsing backup file: " + err.message);
    } finally {
      event.target.value = '';
    }
  };
  reader.readAsText(file);
}

// ----------------- PDF EXPORT -----------------
function exportTargetPDF() {
  const profile = getActiveProfile();
  const target = profile?.targets?.find(t => t.id === currentTargetId);

  if (!target) {
    alert("Please select a target first to generate a PDF report.");
    return;
  }

  const { jsPDF } = window.jspdf;
  const doc = new jsPDF({ orientation: 'portrait', unit: 'pt', format: 'a4' });

  const logs = (target.logs || []).sort((a, b) => a.dayNumber - b.dayNumber);
  const completed = logs.length;
  const pct = Math.min(100, Math.round((completed / target.targetDays) * 100));
  const totalMin = logs.reduce((acc, c) => acc + (c.theoryMinutes || 0) + (c.labMinutes || 0), 0);
  const hours = (totalMin / 60).toFixed(1);
  const commits = logs.filter(l => l.gitPushed).length;
  const dateStr = new Date().toLocaleDateString('en-US', { year: 'numeric', month: 'short', day: 'numeric' });

  // 1. Header Banner
  doc.setFillColor(15, 23, 42);
  doc.rect(0, 0, 595.28, 90, 'F');
  doc.setFont('helvetica', 'bold');
  doc.setFontSize(18);
  doc.setTextColor(255, 255, 255);
  doc.text("Roadmap Tracker - Learning Progress Report", 40, 38);

  doc.setFont('helvetica', 'normal');
  doc.setFontSize(10);
  doc.setTextColor(148, 163, 184);
  doc.text(`Generated on: ${dateStr} | Profile: ${profile.username} (${profile.roleTitle || 'Developer'})`, 40, 60);

  // 2. Metric Cards
  const startY = 110;
  doc.setFillColor(241, 245, 249);
  doc.setDrawColor(203, 213, 225);
  doc.roundedRect(40, startY, 515.28, 55, 6, 6, 'FD');

  doc.setFontSize(9);
  doc.setTextColor(71, 85, 105);
  doc.text("TARGET TITLE", 55, startY + 18);
  doc.text("PROGRESS", 225, startY + 18);
  doc.text("HOURS LOGGED", 335, startY + 18);
  doc.text("GIT COMMITS", 445, startY + 18);

  doc.setFont('helvetica', 'bold');
  doc.setFontSize(11);
  doc.setTextColor(15, 23, 42);
  doc.text(target.title.length > 22 ? target.title.substring(0, 20) + '...' : target.title, 55, startY + 38);
  doc.text(`${completed}/${target.targetDays} (${pct}%)`, 225, startY + 38);
  doc.text(`${hours} hrs`, 335, startY + 38);
  doc.text(`${commits} pushes`, 445, startY + 38);

  // 3. Table Rows
  const tableRows = logs.map(l => [
    `#${l.dayNumber}`,
    l.date,
    l.topic,
    l.notes && l.notes.trim() ? l.notes : "—",
    `${(l.theoryMinutes || 0) + (l.labMinutes || 0)}m`,
    l.gitPushed ? "✓ Pushed" : "Pending"
  ]);

  doc.autoTable({
    startY: startY + 70,
    margin: { left: 40, right: 40 },
    head: [['Day', 'Date', 'Topic / Module', 'Key Learnings & Notes', 'Time', 'Git Status']],
    body: tableRows.length > 0 ? tableRows : [['—', '—', 'No sessions logged yet for this target.', '—', '—', '—']],
    theme: 'striped',
    headStyles: { fillColor: [15, 23, 42], textColor: [255, 255, 255], fontStyle: 'bold', fontSize: 9, cellPadding: 6 },
    bodyStyles: { fontSize: 8, textColor: [30, 41, 59], cellPadding: 6, valign: 'top' },
    columnStyles: {
      0: { cellWidth: 35, fontStyle: 'bold' },
      1: { cellWidth: 65 },
      2: { cellWidth: 120, fontStyle: 'bold' },
      3: { cellWidth: 185 },
      4: { cellWidth: 45, halign: 'center' },
      5: { cellWidth: 65, halign: 'center' }
    },
    alternateRowStyles: { fillColor: [248, 250, 252] },
    didDrawPage: () => {
      doc.setFontSize(8);
      doc.setTextColor(148, 163, 184);
      doc.text(`Page ${doc.internal.getNumberOfPages()}`, 520, 820);
    }
  });

  const sanitizedTitle = target.title.replace(/[^a-z0-9]/gi, '_').toLowerCase();
  doc.save(`${profile.username}_${sanitizedTitle}_report.pdf`);
}

function escapeHtml(str) {
  return (str || '').replace(/[&<>'"]/g, t => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', "'": '&#39;', '"': '&quot;' }[t] || t));
}

// ----------------- INITIALIZE -----------------
initTheme();
loadData();