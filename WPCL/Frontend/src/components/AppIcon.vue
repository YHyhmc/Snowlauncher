<script setup>
defineProps({
  name: { type: String, required: true },
  size: { type: [Number, String], default: 20 }
});
</script>

<template>
  <svg :width="size" :height="size" viewBox="0 0 24 24" fill="none" stroke="currentColor"
    stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
    <template v-if="name === 'home'"><path d="m3 11 9-8 9 8"/><path d="M5 10v10h14V10M9 20v-6h6v6"/></template>
    <template v-else-if="name === 'download'"><path d="M12 3v12m-5-5 5 5 5-5M4 20h16"/></template>
    <template v-else-if="name === 'settings'"><circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.7 1.7 0 0 0 .34 1.88l-2.86 2.86A1.7 1.7 0 0 0 15 19.4 1.7 1.7 0 0 0 14 21h-4a1.7 1.7 0 0 0-1-1.6 1.7 1.7 0 0 0-1.88.34l-2.86-2.86A1.7 1.7 0 0 0 4.6 15 1.7 1.7 0 0 0 3 14v-4a1.7 1.7 0 0 0 1.6-1 1.7 1.7 0 0 0-.34-1.88l2.86-2.86A1.7 1.7 0 0 0 9 4.6 1.7 1.7 0 0 0 10 3h4a1.7 1.7 0 0 0 1 1.6 1.7 1.7 0 0 0 1.88-.34l2.86 2.86A1.7 1.7 0 0 0 19.4 9 1.7 1.7 0 0 0 21 10v4a1.7 1.7 0 0 0-1.6 1z"/></template>
    <template v-else-if="name === 'extension'"><path d="M8.5 3H5a2 2 0 0 0-2 2v3.5a2.5 2.5 0 1 1 0 5V19a2 2 0 0 0 2 2h3.5a2.5 2.5 0 1 1 5 0H19a2 2 0 0 0 2-2v-5.5a2.5 2.5 0 1 1 0-5V5a2 2 0 0 0-2-2h-5.5a2.5 2.5 0 1 1-5 0z"/></template>
    <template v-else-if="name === 'pin'"><path d="m14 4 6 6-3 1-4 4-1 5-2-6-6-2 5-1 4-4zM4 20l5-5"/></template>
    <template v-else-if="name === 'play'"><path d="m8 5 11 7-11 7z"/></template>
    <template v-else-if="name === 'stop'"><rect x="6" y="6" width="12" height="12" rx="2"/></template>
    <template v-else-if="name === 'folder'"><path d="M3 7a2 2 0 0 1 2-2h5l2 2h7a2 2 0 0 1 2 2v9a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"/></template>
    <template v-else-if="name === 'refresh'"><path d="M20 7v5h-5M19 12a7 7 0 1 0-1 5"/></template>
    <template v-else-if="name === 'search'"><circle cx="11" cy="11" r="7"/><path d="m20 20-4-4"/></template>
    <template v-else-if="name === 'chevron'"><path d="m9 6 6 6-6 6"/></template>
    <template v-else-if="name === 'close'"><path d="m6 6 12 12M18 6 6 18"/></template>
    <template v-else-if="name === 'minimize'"><path d="M6 16h12"/></template>
    <template v-else-if="name === 'check'"><path d="m5 12 4 4L19 6"/></template>
    <template v-else-if="name === 'info'"><circle cx="12" cy="12" r="9"/><path d="M12 11v5M12 8h.01"/></template>
    <template v-else-if="name === 'trash'"><path d="M4 7h16M9 7V4h6v3M7 7l1 13h8l1-13M10 11v5M14 11v5"/></template>
    <template v-else-if="name === 'cube'"><path d="m4 7 8-4 8 4-8 4zM4 7v10l8 4 8-4V7M12 11v10"/></template>
    <template v-else-if="name === 'loader'"><path d="M4 8h16M4 16h16M8 4v16M16 4v16"/></template>
    <template v-else-if="name === 'logs'"><path d="M5 4h14v16H5zM8 8h8M8 12h8M8 16h5"/></template>
    <template v-else-if="name === 'user'"><circle cx="12" cy="8" r="4"/><path d="M4.5 21a7.5 7.5 0 0 1 15 0"/></template>
    <template v-else-if="name === 'accounts'"><circle cx="9" cy="8" r="3.5"/><path d="M2.5 20a6.5 6.5 0 0 1 13 0M17 7.5a3 3 0 0 1 0 5.8M17.5 16a5 5 0 0 1 4 4"/></template>
    <template v-else-if="name === 'plus'"><path d="M12 5v14M5 12h14"/></template>
    <template v-else-if="name === 'view'"><path d="M2.5 12s3.5-6 9.5-6 9.5 6 9.5 6-3.5 6-9.5 6-9.5-6-9.5-6z"/><circle cx="12" cy="12" r="2.5"/></template>
  </svg>
</template>
