<script setup>
import { computed, reactive, ref } from "vue";
import AppIcon from "../components/AppIcon.vue";

const props = defineProps({ manifest: Object, installed: Array, loading: Boolean, progress: Object });
const emit = defineEmits(["refresh", "install"]);
const query = ref("");
const expanded = reactive({ release: true, preview: false, april: false, ancient: false });

const aprilVersions = new Set([
  "15w14a", "1.rv-pre1", "3d shareware v1.34", "20w14infinite",
  "22w13oneblockatatime", "23w13a_or_b", "24w14potato", "25w14craftmine"
]);

const groups = [
  { id: "release", label: "正式版", image: "./data/imgs/Grass_Block_44.png" },
  { id: "preview", label: "预览版", image: "./data/imgs/Command_Block_44.png" },
  { id: "april", label: "愚人节版本", image: "./data/imgs/Gold_Block_44.png" },
  { id: "ancient", label: "远古版", image: "./data/imgs/Spawner_44.png" }
];

function categoryOf(version) {
  const id = (version?.id || "").toLowerCase();
  if (aprilVersions.has(id)) return "april";
  if (version?.type === "old_alpha" || version?.type === "old_beta" ||
      (version?.type === "release" && id.startsWith("0."))) return "ancient";
  return version?.type === "release" ? "release" : "preview";
}

function imageFor(version) {
  return groups.find(group => group.id === categoryOf(version))?.image || groups[1].image;
}

function releaseStamp(version) {
  if (!version?.releaseTime) return "发布时间未知";
  return `发布时间 ${new Date(version.releaseTime).toLocaleString("zh-CN", {
    year: "numeric", month: "2-digit", day: "2-digit", hour: "2-digit", minute: "2-digit", hour12: false
  })}`;
}

const versions = computed(() => props.manifest?.versions || []);
const installedIds = computed(() => new Set((props.installed || []).map(item => item.id)));
const latestRelease = computed(() => versions.value.find(item => categoryOf(item) === "release") || null);
const latestPreview = computed(() => versions.value.find(item => categoryOf(item) === "preview") || null);

const groupedVersions = computed(() => {
  const keyword = query.value.trim().toLowerCase();
  const featured = new Set([latestRelease.value?.id, latestPreview.value?.id].filter(Boolean));
  const result = Object.fromEntries(groups.map(group => [group.id, []]));
  for (const version of versions.value) {
    if (featured.has(version.id)) continue;
    if (keyword && !version.id.toLowerCase().includes(keyword)) continue;
    result[categoryOf(version)].push(version);
  }
  return result;
});

function choose(version) {
  if (version) emit("install", version);
}
</script>

<template>
  <section class="content-view download-view repair-download">
    <header class="download-heading">
      <div>
        <span>下载游戏</span>
      </div>
      <button class="icon-button" title="刷新官方版本列表" :disabled="loading" @click="emit('refresh')">
        <AppIcon name="refresh" :size="18" />
      </button>
    </header>

    <div v-if="progress" class="download-progress repair-progress">
      <div><strong>正在安装</strong><span>{{ progress.currentItem || progress.stage }}</span></div>
      <div class="progress-line"><i :style="{ width: `${progress.total ? progress.completed / progress.total * 100 : 4}%` }"></i></div>
      <small>{{ progress.completed }} / {{ progress.total || "—" }}</small>
    </div>

    <div class="repair-search-row">
      <label class="search-box repair-search">
        <AppIcon name="search" :size="18" />
        <input v-model="query" placeholder="搜索版本" aria-label="搜索 Minecraft 版本" />
      </label>
      <span>{{ loading ? "正在获取版本列表…" : `${versions.length} 个官方版本` }}</span>
    </div>

    <div class="repair-catalog">
      <section class="catalog-section featured-section">
        <h2>最新发布</h2>
        <div class="featured-card">
          <button v-for="(item, index) in [latestRelease, latestPreview]" :key="item?.id || `featured-${index}`" :disabled="!item" @click="choose(item)">
            <img :src="imageFor(item)" alt="" />
            <span>
              <small>{{ item && categoryOf(item) === 'release' ? '最新正式版' : '最新预览版' }}</small>
              <strong>{{ item?.id || "暂无版本" }}</strong>
              <em>{{ item ? releaseStamp(item) : "尚未获取" }}</em>
            </span>
            <b v-if="item && installedIds.has(item.id)">已安装</b>
          </button>
          <div v-if="loading" class="catalog-loading"><i></i></div>
        </div>
      </section>

      <section class="catalog-section other-section">
        <h2>其他</h2>
        <div class="group-card">
          <section v-for="group in groups" :key="group.id" class="version-group">
            <button class="group-heading" :aria-expanded="expanded[group.id]" @click="expanded[group.id] = !expanded[group.id]">
              <span>{{ group.label }} <small>（{{ groupedVersions[group.id].length }}）</small></span>
              <AppIcon name="chevron" :size="15" />
            </button>
            <Transition name="catalog-fold">
              <div v-if="expanded[group.id]" class="group-rows">
                <button v-for="version in groupedVersions[group.id]" :key="version.id" class="catalog-version" @click="choose(version)">
                  <img :src="group.image" alt="" />
                  <span><strong>{{ version.id }}</strong><small>{{ releaseStamp(version) }}</small></span>
                  <b v-if="installedIds.has(version.id)">已安装</b>
                </button>
                <p v-if="!groupedVersions[group.id].length">{{ query ? "没有匹配的版本" : "暂无版本" }}</p>
              </div>
            </Transition>
          </section>
        </div>
      </section>
    </div>
  </section>
</template>
