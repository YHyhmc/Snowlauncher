<script setup>
import AppIcon from "./AppIcon.vue";

defineProps({ toast: { type: Object, default: null } });

const iconNames = { success: "check", error: "close", info: "info" };
</script>

<template>
  <div
    v-if="toast"
    class="toast"
    :class="[toast.type, { show: toast.show }]"
    role="status"
    aria-live="polite"
    aria-atomic="true"
  >
    <span class="toast-icon"><AppIcon :name="iconNames[toast.type]" :size="14" /></span>
    <span>{{ toast.message }}</span>
  </div>
</template>
