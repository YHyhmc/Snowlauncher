import { onBeforeUnmount, ref } from "vue";

const SHOW_DURATION = 2700;
const REMOVE_DURATION = 300;

export function useToast() {
  const toast = ref(null);
  let hideTimer = null;
  let removeTimer = null;

  function clearTimers() {
    if (hideTimer) clearTimeout(hideTimer);
    if (removeTimer) clearTimeout(removeTimer);
    hideTimer = null;
    removeTimer = null;
  }

  function showToast(message, type = "info") {
    clearTimers();
    toast.value = { show: true, message, type };

    hideTimer = setTimeout(() => {
      if (toast.value) toast.value = { ...toast.value, show: false };
      hideTimer = null;
    }, SHOW_DURATION);

    removeTimer = setTimeout(() => {
      toast.value = null;
      removeTimer = null;
    }, SHOW_DURATION + REMOVE_DURATION);
  }

  onBeforeUnmount(clearTimers);
  return { toast, showToast };
}
