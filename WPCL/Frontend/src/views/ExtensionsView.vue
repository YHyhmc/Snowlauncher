<script setup>
import AppIcon from "../components/AppIcon.vue";
defineProps({ versions: Array });
defineEmits(["open-version", "go-download"]);
</script>

<template>
  <section class="content-view extensions-view">
    <header class="page-heading"><div><span>扩展管理</span><h1>Mod 环境</h1><p>查看加载器版本并进入对应的独立游戏目录。</p></div></header>
    <div class="loader-strip">
      <div class="loader-mark fabric">F</div><div><strong>Fabric</strong><small>轻量、快速</small></div>
      <div class="loader-mark neo">N</div><div><strong>NeoForge</strong><small>现代社区加载器</small></div>
      <div class="loader-mark forge">A</div><div><strong>Forge</strong><small>经典生态</small></div>
      <button @click="$emit('go-download')">安装加载器 <AppIcon name="chevron" :size="14" /></button>
    </div>
    <div class="extension-list">
      <header><h2>已安装环境</h2><span>{{ versions?.filter(item => item.loader !== 'Vanilla').length || 0 }} 个</span></header>
      <button v-for="version in versions?.filter(item => item.loader !== 'Vanilla')" :key="version.id" @click="$emit('open-version', version.id)">
        <span class="env-icon"><AppIcon name="loader" :size="18" /></span>
        <span><strong>{{ version.id }}</strong><small>{{ version.loader }} {{ version.loaderVersion || '' }} · {{ version.pclCompatible ? 'PCL2 可识别' : '普通版本' }}</small></span>
        <AppIcon name="folder" :size="17" />
      </button>
      <div v-if="!versions?.some(item => item.loader !== 'Vanilla')" class="empty-environment"><AppIcon name="cube" :size="28" /><strong>尚未安装 Mod 加载器</strong><p>在下载页选择一个版本后即可安装。</p></div>
    </div>
  </section>
</template>
