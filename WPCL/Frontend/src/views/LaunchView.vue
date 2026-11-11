<script setup>
import AppIcon from "../components/AppIcon.vue";

defineProps({ settings: Object, account: Object, selectedVersion: Object, running: Boolean, busy: Boolean });
defineEmits(["launch", "stop", "settings", "choose-version", "choose-account"]);
</script>

<template>
  <section class="launch-view">
    <div class="launch-center">
      <button class="launch-account-selector" title="切换账户" @click="$emit('choose-account')">
        <span class="player-avatar launch-avatar">
          <img v-if="account?.avatarSvg" :src="account.avatarSvg" alt="玩家头像" />
          <AppIcon v-else name="user" :size="32" />
        </span>
        <span class="launch-account-name">{{ account?.username || settings?.username || "Player" }}<AppIcon name="chevron" :size="14" /></span>
      </button>
      <button class="launch-main" :disabled="busy || (!selectedVersion && !running)" @click="running ? $emit('stop') : $emit('launch')">
        {{ running ? "结束游戏" : busy ? "正在准备" : "启动游戏" }}
      </button>
      <button class="version-selector" :class="{ empty: !selectedVersion }" @click="$emit('choose-version')">
        <span>
          <strong>{{ selectedVersion?.id || "选择游戏版本" }}</strong>
          <small v-if="!selectedVersion">没有可启动的版本</small>
        </span>
        <AppIcon name="chevron" :size="15" />
      </button>
      <button class="game-settings" @click="$emit('settings')">游戏设置 &gt;</button>
    </div>
  </section>
</template>
