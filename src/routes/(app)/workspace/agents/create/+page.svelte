<script lang="ts">
	import { getContext } from 'svelte';
	import { toast } from 'svelte-sonner';
	import { goto } from '$app/navigation';

	import { createAgent, type AgentDefinition } from '$lib/apis/a2a';
	import AgentEditor from '$lib/components/workspace/Agents/AgentEditor.svelte';

	const i18n = getContext('i18n');

	const onSubmit = async (agent: AgentDefinition) => {
		const res = await createAgent(localStorage.token, agent);
		if (res) {
			toast.success($i18n.t('Agent aangemaakt'));
			await goto(`/workspace/agents/edit?id=${encodeURIComponent(res.id)}`);
		}
	};
</script>

<AgentEditor {onSubmit} />
