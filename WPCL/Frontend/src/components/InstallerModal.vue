<script setup>
import { computed, ref, watch } from "vue";
import AppIcon from "./AppIcon.vue";

const props = defineProps({ version: Object, open: Boolean, loaderVersions: Array, loading: Boolean, installing: Boolean });
const emit = defineEmits(["close", "loader-change", "install"]);
const selectedLoader = ref("Vanilla");
const selectedLoaderVersion = ref("");
const minecraftImage = "./data/imgs/Grass_Block_44.png";

const loaders = [
  { id: "Vanilla", name: "保持原版", desc: "仅安装 Minecraft，不添加 Mod 加载器", image: "./data/imgs/Grass_Block_44.png" },
  { id: "Fabric", name: "Fabric", desc: "轻量、快速的 Mod 加载器", image: "./data/imgs/Fabric_Logo.png" },
  { id: "NeoForge", name: "NeoForge", desc: "面向现代版本的社区加载器", image: "./data/imgs/NeoForge_Logo.png" },
  { id: "Forge", name: "Forge", desc: "经典且兼容广泛的 Mod 加载器", image: "./data/imgs/Anvil.png" }
];

const canInstall = computed(() => selectedLoader.value === "Vanilla" || Boolean(selectedLoaderVersion.value));
const selectedName = computed(() => loaders.find(item => item.id === selectedLoader.value)?.name || "保持原版");

watch(() => props.open, value => {
  if (!value) return;
  selectedLoader.value = "Vanilla";
  selectedLoaderVersion.value = "";
});

watch(() => props.loaderVersions, versions => {
  selectedLoaderVersion.value = versions?.find(item => item.stable)?.version || versions?.[0]?.version || "";
});

function chooseLoader(loader) {
  selectedLoader.value = loader;
  selectedLoaderVersion.value = "";
  if (loader !== "Vanilla") emit("loader-change", loader);
}

function submit() {
  if (!canInstall.value) return;
  emit("install", { versionId: props.version.id, loader: selectedLoader.value, loaderVersion: selectedLoaderVersion.value || null });
}
</script>

<template>
  <Transition name="modal">
    <div v-if="open" class="modal-layer" role="dialog" aria-modal="true" aria-label="安装版本">
      <button class="modal-backdrop" aria-label="关闭" @click="emit('close')"></button>
      <section class="installer-modal repair-installer">
        <header>
          <button class="installer-back" @click="emit('close')"><AppIcon name="chevron" :size="15" /> 返回版本选择</button>
          <button class="modal-close" aria-label="关闭" @click="emit('close')"><AppIcon name="close" :size="17" /></button>
        </header>

        <div class="selected-download-version">
          <img :src="minecraftImage" alt="Minecraft 草方块" />
          <span><strong>Minecraft {{ version?.id }}</strong><small>官方版本</small></span>
          <div><small>下载内容</small><strong>{{ selectedName }}</strong></div>
        </div>

        <div class="loader-heading">
          <h2>选择 Mod 加载器</h2>
          <p>只会安装你明确选择的加载器版本。</p>
        </div>

        <div class="loader-choices repair-loader-choices">
          <button v-for="loader in loaders" :key="loader.id" :class="{ selected: selectedLoader === loader.id }" @click="chooseLoader(loader.id)">
            <img :src="loader.image" :alt="`${loader.name} 图标`" />
            <div><strong>{{ loader.name }}</strong><small>{{ loader.desc }}</small></div>
            <i><AppIcon v-if="selectedLoader === loader.id" name="check" :size="13" /></i>
          </button>
        </div>

        <label v-if="selectedLoader !== 'Vanilla'" class="loader-version-select">
          加载器版本
          <select v-model="selectedLoaderVersion" :disabled="loading">
            <option v-if="loading" value="">正在读取官方版本…</option>
            <option v-for="item in loaderVersions" :key="item.version" :value="item.version">{{ item.displayName }}{{ item.stable ? '' : '（测试版）' }}</option>
          </select>
        </label>

        <footer>
          <p>确认后将进入下载任务；兼容模式开启时同时生成 PCL2 配置。</p>
          <button :disabled="!canInstall || installing" @click="submit">{{ installing ? "安装中" : "确认下载" }}</button>
        </footer>
      </section>
    </div>
  </Transition>
</template>
