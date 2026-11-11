<script setup>
import AppIcon from "./AppIcon.vue";
import { bridge } from "../services/bridge";

function drag(event) {
  if (event.button === 0 && !event.target.closest("button")) bridge.request("windowDrag").catch(() => {});
}
</script>

<template>
  <header class="titlebar" @mousedown="drag">
    <span class="brand">WPCL</span>
    <div class="window-actions">
      <button aria-label="最小化" @click="bridge.request('windowAction', 'minimize')"><AppIcon name="minimize" :size="15" /></button>
      <button class="close-window" aria-label="关闭" @click="bridge.request('windowAction', 'close')"><AppIcon name="close" :size="15" /></button>
    </div>
  </header>
</template>
