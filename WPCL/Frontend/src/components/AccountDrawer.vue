<script setup>
import AppIcon from "./AppIcon.vue";

defineProps({ open: Boolean, account: Object, pinned: Boolean, busy: Boolean });
defineEmits(["select", "add", "manage", "toggle-pin"]);
</script>

<template>
  <aside class="account-drawer" :class="{ open }" aria-label="账户列表">
    <header>
      <h2>账户</h2>
      <button :class="{ active: pinned }" title="固定账户列表" @click="$emit('toggle-pin')"><AppIcon name="pin" :size="17" /></button>
    </header>
    <div class="account-drawer-list">
      <button v-for="item in account?.accounts || []" :key="item.id" class="account-drawer-item"
        :class="{ selected: item.selected }" :disabled="busy" @click="$emit('select', item.id)">
        <span class="player-avatar drawer-account-avatar">
          <img v-if="item.avatarSvg" :src="item.avatarSvg" alt="" />
          <AppIcon v-else name="user" :size="18" />
        </span>
        <span class="account-drawer-copy"><strong>{{ item.username }}</strong><small>{{ item.uuid }}</small></span>
        <AppIcon v-if="item.selected" name="check" :size="16" />
      </button>
      <div v-if="!account?.accounts?.length" class="drawer-empty">
        <strong>暂无正版账户</strong>
        <span>添加 Microsoft 账户后可在这里快速切换。</span>
      </div>
    </div>
    <footer>
      <button :disabled="busy" @click="$emit('add')"><AppIcon name="plus" :size="15" />添加账户</button>
      <button :disabled="busy" @click="$emit('manage')"><AppIcon name="accounts" :size="15" />管理账户</button>
    </footer>
  </aside>
</template>
