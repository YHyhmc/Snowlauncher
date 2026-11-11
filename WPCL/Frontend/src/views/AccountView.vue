<script setup>
import { ref, watch } from "vue";
import AppIcon from "../components/AppIcon.vue";

const props = defineProps({ account: Object, busy: Boolean });
const emit = defineEmits(["login", "select", "offline", "logout"]);
const offlineUsername = ref("Player");

watch(() => props.account, value => {
  offlineUsername.value = value?.offlineUsername || (value?.mode === "offline" ? value?.username : "") || "Player";
}, { immediate: true });
</script>

<template>
  <section class="content-view account-view">
    <header class="page-heading">
      <div><span>账户管理</span><h1>玩家身份</h1><p>离线账户与 Microsoft 正版账户在这里独立管理。</p></div>
    </header>

    <div class="account-current">
      <div class="player-avatar account-avatar">
        <img v-if="account?.avatarSvg" :src="account.avatarSvg" alt="当前玩家头像" />
        <AppIcon v-else name="user" :size="30" />
      </div>
      <div><small>{{ account?.signedIn ? "MICROSOFT" : "OFFLINE" }}</small><strong>{{ account?.username || "Player" }}</strong><span>{{ account?.uuid || "尚未选择账户" }}</span></div>
    </div>

    <section class="account-section">
      <header><div><h2>Microsoft 正版账户</h2><p>添加多个账户，并在启动前快速切换当前身份。</p></div></header>
      <div class="account-add-row">
        <button class="account-login" :disabled="busy" @click="emit('login')">
          <AppIcon name="plus" :size="18" />{{ busy ? "正在处理" : "添加 Microsoft 账户" }}
        </button>
        <span>已保存 {{ account?.accounts?.length || 0 }} 个账户</span>
      </div>
      <p class="account-hint">登录信息由 Snow launcher 独立保存；切换账户时会自动刷新正版会话。</p>

      <div v-if="account?.accounts?.length" class="saved-accounts">
        <div v-for="item in account.accounts" :key="item.id" :class="['saved-account-row', { selected: item.selected }]">
          <button :disabled="busy" @click="emit('select', item.id)">
            <span class="player-avatar saved-avatar"><img v-if="item.avatarSvg" :src="item.avatarSvg" alt="" /><AppIcon v-else name="user" :size="18" /></span>
            <span><strong>{{ item.username }}</strong><small>{{ item.uuid }}</small></span>
            <AppIcon v-if="item.selected" name="check" :size="17" />
          </button>
          <button class="account-remove" title="退出并移除账户" :disabled="busy" @click="emit('logout', item.id)"><AppIcon name="trash" :size="15" /></button>
        </div>
      </div>
    </section>

    <section class="account-section offline-section">
      <header><div><h2>离线账户</h2><p>仅用于不需要正版验证的本地启动。</p></div></header>
      <label class="account-field">玩家名称
        <span><input v-model.trim="offlineUsername" maxlength="16" :disabled="busy" /><button :disabled="busy" @click="emit('offline', offlineUsername)">使用</button></span>
      </label>
    </section>
  </section>
</template>
