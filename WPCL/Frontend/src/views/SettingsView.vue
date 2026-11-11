<script setup>
import AppIcon from "../components/AppIcon.vue";

const props = defineProps({ modelValue: Object, configPath: String, saving: Boolean });
const emit = defineEmits(["update:modelValue", "choose-directory", "save", "open-directory"]);

function updateBackgroundOpacity(event) {
  const value = Math.min(100, Math.max(0, Number(event.target.value)));
  emit("update:modelValue", { ...props.modelValue, backgroundOpacity: value });
}
</script>

<template>
  <section class="content-view settings-view">
    <header class="page-heading"><div><span>启动设置</span><h1>游戏与兼容</h1><p>管理运行环境和 PCL2 目录结构。</p></div></header>
    <form class="settings-form" @submit.prevent="emit('save')">
      <section class="settings-group wide">
        <header><AppIcon name="folder" :size="19" /><div><h2>游戏目录</h2><p>可直接选择 PCL2 正在使用的 .minecraft 目录。</p></div></header>
        <label>目录位置
          <span class="input-with-action"><input :value="modelValue.gameDirectory" readonly /><button type="button" @click="emit('choose-directory')">选择</button></span>
        </label>
        <label class="switch-row"><span><strong>PCL2 兼容模式</strong><small>安装后生成 PCL/Setup.ini，并按 PCL2 独立版本目录启动。</small></span><input type="checkbox" :checked="modelValue.pcl2Compatibility" @change="emit('update:modelValue', { ...modelValue, pcl2Compatibility: $event.target.checked })" /><i></i></label>
        <div class="config-path"><span>PCLjson 配置</span><code>{{ configPath }}</code></div>
      </section>
      <section class="settings-group">
        <header><AppIcon name="settings" :size="19" /><div><h2>Java 与内存</h2><p>Forge 和 NeoForge 安装器也会使用此 Java。</p></div></header>
        <label>Java 路径<input :value="modelValue.javaPath" @input="emit('update:modelValue', { ...modelValue, javaPath: $event.target.value })" /></label>
        <label class="switch-row"><span><strong>自动分配内存</strong><small>根据设备内存选择合适上限。</small></span><input type="checkbox" :checked="modelValue.autoMemory" @change="emit('update:modelValue', { ...modelValue, autoMemory: $event.target.checked })" /><i></i></label>
        <div v-if="!modelValue.autoMemory" class="memory-grid">
          <label>最小<input :value="modelValue.minimumMemory" @input="emit('update:modelValue', { ...modelValue, minimumMemory: $event.target.value })" /></label>
          <label>最大<input :value="modelValue.maximumMemory" @input="emit('update:modelValue', { ...modelValue, maximumMemory: $event.target.value })" /></label>
        </div>
      </section>
      <section class="settings-group appearance-group">
        <header><AppIcon name="view" :size="19" /><div><h2>界面透明度</h2><p>调整启动器背景浓度，修改时会立即预览。</p></div></header>
        <label class="opacity-control">
          <span><strong>背景不透明度</strong><small>数值越低，桌面内容越清晰。</small></span>
          <output>{{ modelValue.backgroundOpacity ?? 30 }}%</output>
          <input type="range" min="0" max="100" step="1" :value="modelValue.backgroundOpacity ?? 30"
            :style="{ '--range-progress': `${modelValue.backgroundOpacity ?? 30}%` }" @input="updateBackgroundOpacity" />
        </label>
      </section>
      <footer><button type="button" class="secondary" @click="emit('open-directory')">打开游戏目录</button><button class="primary" :disabled="saving">{{ saving ? "保存中" : "保存设置" }}</button></footer>
    </form>
  </section>
</template>
