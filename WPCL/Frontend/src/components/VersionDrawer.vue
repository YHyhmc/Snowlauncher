<script setup>
import AppIcon from "./AppIcon.vue";

defineProps({ open: Boolean, versions: Array, selectedId: String, pinned: Boolean });
defineEmits(["select", "toggle-pin", "download"]);

const loaderImages = {
  Vanilla: "./data/imgs/Grass_Block_44.png",
  Fabric: "./data/imgs/Fabric_Logo.png",
  NeoForge: "./data/imgs/NeoForge_Logo.png",
  Forge: "./data/imgs/Anvil.png"
};

const aprilVersions = new Set([
  "15w14a", "1.rv-pre1", "3d shareware v1.34", "20w14infinite",
  "22w13oneblockatatime", "23w13a_or_b", "24w14potato", "25w14craftmine"
]);

function versionImage(version) {
  const loader = String(version?.loader || "Vanilla");
  if (loader !== "Vanilla") return loaderImages[loader] || loaderImages.Vanilla;

  const id = String(version?.id || "").toLowerCase();
  if (aprilVersions.has(id)) return "./data/imgs/Gold_Block_44.png";
  if (/^(?:\d{2}w\d{2}[a-z]|.*-(?:pre|rc)\d+)$/i.test(id) || id.includes("snapshot")) {
    return "./data/imgs/Command_Block_44.png";
  }
  if (/^(?:rd-|a|b|infdev|indev)/i.test(id) || id.startsWith("0.")) {
    return "./data/imgs/Spawner_44.png";
  }
  return loaderImages.Vanilla;
}

function isLoaderLogo(version) {
  return String(version?.loader || "Vanilla") !== "Vanilla";
}

function loaderLabel(version) {
  if (!version || version.loader === "Vanilla") return version?.id || "原版";
  return `${version.baseVersion || version.id} ${version.loader} ${version.loaderVersion || ""}`.trim();
}
</script>

<template>
  <aside class="version-drawer" :class="{ open }" aria-label="版本列表">
    <header>
      <h2>主页</h2>
      <button :class="{ active: pinned }" title="固定版本列表" @click="$emit('toggle-pin')"><AppIcon name="pin" :size="17" /></button>
    </header>
    <div class="version-list">
      <button v-for="version in versions" :key="version.id" class="version-item"
        :class="{ selected: version.id === selectedId, incomplete: !version.isLaunchable }" @click="$emit('select', version.id)">
        <img class="version-image" :class="{ logo: isLoaderLogo(version) }" :src="versionImage(version)" alt="" />
        <span class="version-copy">
          <strong>{{ version.id }}</strong>
          <small>{{ version.isLaunchable ? loaderLabel(version) : version.launchIssue }}</small>
        </span>
      </button>
      <div v-if="!versions?.some(version => version.isLaunchable)" class="drawer-empty">
        <strong>{{ versions?.length ? "没有完整版本" : "暂无可启动版本" }}</strong>
        <span>{{ versions?.length ? "现有版本缺少必要文件，请重新下载。" : "先下载一个 Minecraft 版本。" }}</span>
        <button @click="$emit('download')">前往下载</button>
      </div>
    </div>
  </aside>
</template>
