<script setup>
import { computed, onBeforeUnmount, onMounted, ref } from "vue";
import AcrylicTexture from "./components/AcrylicTexture.vue";
import AccountDrawer from "./components/AccountDrawer.vue";
import AppIcon from "./components/AppIcon.vue";
import InstallerModal from "./components/InstallerModal.vue";
import TitleBar from "./components/TitleBar.vue";
import Toast from "./components/Toast.vue";
import VersionDrawer from "./components/VersionDrawer.vue";
import AccountView from "./views/AccountView.vue";
import DownloadView from "./views/DownloadView.vue";
import ExtensionsView from "./views/ExtensionsView.vue";
import LaunchView from "./views/LaunchView.vue";
import SettingsView from "./views/SettingsView.vue";
import { useToast } from "./composables/useToast";
import { bridge } from "./services/bridge";

const page = ref("launch");
const initialized = ref(false);
const settings = ref(null);
const settingsDraft = ref(null);
const configPath = ref("");
const installed = ref([]);
const selectedId = ref("");
const manifest = ref(null);
const manifestLoading = ref(false);
const gameRunning = ref(false);
const operationActive = ref(false);
const progress = ref(null);
const versionDrawerPinned = ref(false);
const versionDrawerVisible = ref(false);
const accountDrawerPinned = ref(false);
const accountDrawerVisible = ref(false);
const installVersion = ref(null);
const loaderVersions = ref([]);
const loadersLoading = ref(false);
const saving = ref(false);
const account = ref(null);
const accountBusy = ref(false);
const { toast, showToast } = useToast();

const selectedVersion = computed(() => installed.value.find(item => item.id === selectedId.value && item.isLaunchable) || null);
const versionDrawerOpen = computed(() => page.value === "launch" && (versionDrawerPinned.value || versionDrawerVisible.value));
const accountDrawerOpen = computed(() => page.value === "launch" && (accountDrawerPinned.value || accountDrawerVisible.value));
const drawerOpen = computed(() => versionDrawerOpen.value || accountDrawerOpen.value);
const backgroundOpacity = computed(() => {
  const value = Number(settingsDraft.value?.backgroundOpacity ?? 30);
  return Math.min(100, Math.max(0, Number.isFinite(value) ? value : 30));
});

const navigation = [
  { id: "launch", label: "启动", icon: "home" },
  { id: "download", label: "下载", icon: "download" },
  { id: "account", label: "账户", icon: "accounts" },
  { id: "settings", label: "设置", icon: "settings" },
  { id: "extensions", label: "扩展", icon: "extension" }
];

function selectPage(id) {
  if (id === "launch" && page.value === "launch") {
    accountDrawerVisible.value = false;
    accountDrawerPinned.value = false;
    versionDrawerVisible.value = !versionDrawerVisible.value;
  }
  else page.value = id;
  if (id === "download" && !manifest.value) refreshManifest();
}

function openVersionSelector() {
  page.value = "launch";
  accountDrawerPinned.value = false;
  accountDrawerVisible.value = false;
  if (versionDrawerOpen.value) {
    versionDrawerPinned.value = false;
    versionDrawerVisible.value = false;
  } else {
    versionDrawerVisible.value = true;
  }
}

function openAccountSelector() {
  page.value = "launch";
  versionDrawerPinned.value = false;
  versionDrawerVisible.value = false;
  if (accountDrawerOpen.value) {
    accountDrawerPinned.value = false;
    accountDrawerVisible.value = false;
  } else {
    accountDrawerVisible.value = true;
  }
}

function selectInstalledVersion(id) {
  const version = installed.value.find(item => item.id === id);
  if (!version?.isLaunchable) {
    showToast(version?.launchIssue || "此版本无法启动。", "error");
    return;
  }
  selectedId.value = id;
  if (!versionDrawerPinned.value) versionDrawerVisible.value = false;
}

async function bootstrap() {
  try {
    const data = await bridge.request("bootstrap");
    settings.value = data.settings;
    settingsDraft.value = structuredClone(data.settings);
    installed.value = data.installedVersions || [];
    configPath.value = data.configPath || "";
    gameRunning.value = data.gameRunning;
    account.value = data.account;
    selectedId.value = data.settings.lastVersion && installed.value.some(item => item.id === data.settings.lastVersion && item.isLaunchable)
      ? data.settings.lastVersion
      : installed.value.find(item => item.isLaunchable)?.id || "";
  } catch (error) {
    showToast(error.message, "error");
  } finally {
    initialized.value = true;
  }
}

async function refreshManifest() {
  manifestLoading.value = true;
  try { manifest.value = await bridge.request("refreshManifest"); }
  catch (error) { showToast(error.message, "error"); }
  finally { manifestLoading.value = false; }
}

async function launch() {
  if (!selectedId.value) return showToast("请先选择已安装版本。", "info");
  operationActive.value = true;
  try {
    await bridge.request("launchVersion", { versionId: selectedId.value });
    gameRunning.value = true;
    showToast("游戏已启动。", "success");
  } catch (error) { showToast(error.message, "error"); }
  finally { operationActive.value = false; }
}

async function stop() {
  try { await bridge.request("stopGame"); showToast("已请求结束游戏。", "info"); }
  catch (error) { showToast(error.message, "error"); }
}

async function chooseDirectory() {
  try {
    const directory = await bridge.request("chooseDirectory");
    if (directory) settingsDraft.value = { ...settingsDraft.value, gameDirectory: directory };
  } catch (error) { showToast(error.message, "error"); }
}

async function saveSettings() {
  saving.value = true;
  try {
    const data = await bridge.request("saveSettings", settingsDraft.value);
    settings.value = data.settings;
    settingsDraft.value = structuredClone(data.settings);
    installed.value = data.installedVersions;
    if (!installed.value.some(item => item.id === selectedId.value && item.isLaunchable)) {
      selectedId.value = installed.value.find(item => item.isLaunchable)?.id || "";
    }
    showToast("设置已保存到 PCLjson。", "success");
  } catch (error) { showToast(error.message, "error"); }
  finally { saving.value = false; }
}

function applyAccountState(state) {
  account.value = state;
  const accountSettings = { username: state.username, uuid: state.uuid, useMicrosoftAccount: state.signedIn, selectedAccountId: state.selectedAccountId, offlineUsername: state.offlineUsername };
  if (settings.value) settings.value = { ...settings.value, ...accountSettings };
  if (settingsDraft.value) settingsDraft.value = { ...settingsDraft.value, ...accountSettings };
}

async function accountAction(action, payload, successMessage) {
  accountBusy.value = true;
  try {
    const state = await bridge.request(action, payload);
    applyAccountState(state);
    if (successMessage) showToast(successMessage, "success");
    return state;
  } catch (error) { showToast(error.message, "error"); return null; }
  finally { accountBusy.value = false; }
}

const loginMicrosoft = () => accountAction("loginMicrosoft", null, "新账户已添加并切换。");
const selectAccount = accountId => accountAction("selectAccount", { accountId }, "已切换 Microsoft 账户。");
const useOfflineAccount = username => accountAction("useOfflineAccount", { username }, "已切换到离线账户。");
const logoutAccount = accountId => accountAction("logoutAccount", { accountId }, "账户已退出。");

async function selectAccountFromDrawer(accountId) {
  const state = await selectAccount(accountId);
  if (state && !accountDrawerPinned.value) accountDrawerVisible.value = false;
}

function openAccountManagement() {
  accountDrawerPinned.value = false;
  accountDrawerVisible.value = false;
  page.value = "account";
}

function openInstaller(version) {
  installVersion.value = version;
  loaderVersions.value = [];
}

async function loadLoaderVersions(loader) {
  loadersLoading.value = true;
  loaderVersions.value = [];
  try {
    const data = await bridge.request("getLoaderVersions", { versionId: installVersion.value.id, loader });
    loaderVersions.value = data.versions || [];
    if (!loaderVersions.value.length) showToast(`没有找到适用于 ${installVersion.value.id} 的 ${loader} 版本。`, "info");
  } catch (error) { showToast(error.message, "error"); }
  finally { loadersLoading.value = false; }
}

async function install(request) {
  operationActive.value = true;
  try {
    const result = await bridge.request("installVersion", request);
    installed.value = result.installedVersions || installed.value;
    selectedId.value = result.installedVersionId || request.versionId;
    installVersion.value = null;
    showToast("版本安装完成。", "success");
  } catch (error) { showToast(error.message, "error"); }
  finally { operationActive.value = false; progress.value = null; }
}

async function openDirectory(action, versionId = null) {
  try { await bridge.request(action, versionId ? { versionId } : null); }
  catch (error) { showToast(error.message, "error"); }
}

function handleBridgeEvent(name, data) {
  if (name === "installedChanged") installed.value = data || [];
  if (name === "progress") progress.value = data;
  if (name === "operationState") operationActive.value = data.active;
  if (name === "gameState") gameRunning.value = data.running;
  if (name === "accountChanged") applyAccountState(data);
  if (name === "log" && data.level === "error") showToast(data.message, "error");
}

let unsubscribe;
onMounted(() => { unsubscribe = bridge.subscribe(handleBridgeEvent); bootstrap(); });
onBeforeUnmount(() => unsubscribe?.());
</script>

<template>
  <div class="app-shell" :style="{ '--app-background-opacity': `${backgroundOpacity}%` }">
    <div class="acrylic-backdrop" aria-hidden="true"></div>
    <AcrylicTexture />
    <TitleBar />
    <nav class="rail" aria-label="主导航">
      <button v-for="item in navigation" :key="item.id" :class="{ active: page === item.id }" :title="item.label" @click="selectPage(item.id)">
        <AppIcon :name="item.icon" :size="19" /><span>{{ item.label }}</span>
      </button>
    </nav>
    <VersionDrawer :open="versionDrawerOpen" :versions="installed" :selected-id="selectedId" :pinned="versionDrawerPinned"
      @select="selectInstalledVersion" @toggle-pin="versionDrawerPinned = !versionDrawerPinned" @download="selectPage('download')" />
    <AccountDrawer :open="accountDrawerOpen" :account="account" :pinned="accountDrawerPinned" :busy="accountBusy"
      @select="selectAccountFromDrawer" @add="loginMicrosoft" @manage="openAccountManagement"
      @toggle-pin="accountDrawerPinned = !accountDrawerPinned" />

    <main :class="['main-stage', { 'drawer-shift': drawerOpen }]">
      <Transition name="page" mode="out-in">
        <LaunchView v-if="page === 'launch'" key="launch" :settings="settings" :account="account" :selected-version="selectedVersion"
          :running="gameRunning" :busy="operationActive" @launch="launch" @stop="stop" @settings="page = 'settings'"
          @choose-version="openVersionSelector" @choose-account="openAccountSelector" />
        <DownloadView v-else-if="page === 'download'" key="download" :manifest="manifest" :installed="installed"
          :loading="manifestLoading" :progress="progress" @refresh="refreshManifest" @install="openInstaller" />
        <AccountView v-else-if="page === 'account'" key="account" :account="account" :busy="accountBusy"
          @login="loginMicrosoft" @select="selectAccount" @offline="useOfflineAccount" @logout="logoutAccount" />
        <SettingsView v-else-if="page === 'settings' && settingsDraft" key="settings" v-model="settingsDraft"
          :config-path="configPath" :saving="saving" @choose-directory="chooseDirectory" @save="saveSettings"
          @open-directory="openDirectory('openGameDirectory')" />
        <ExtensionsView v-else-if="page === 'extensions'" key="extensions" :versions="installed" @go-download="selectPage('download')"
          @open-version="id => openDirectory('openVersionDirectory', id)" />
      </Transition>
    </main>

    <div v-if="!initialized" class="startup-overlay"><span></span><p>正在读取游戏目录</p></div>
    <InstallerModal :open="Boolean(installVersion)" :version="installVersion" :loader-versions="loaderVersions"
      :loading="loadersLoading" :installing="operationActive" @close="installVersion = null"
      @loader-change="loadLoaderVersions" @install="install" />
    <Toast :toast="toast" />
  </div>
</template>
