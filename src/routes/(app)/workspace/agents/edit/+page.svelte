<script lang="ts">
	import { getContext, onMount } from 'svelte';
	import { toast } from 'svelte-sonner';
	import { goto } from '$app/navigation';
	import { page } from '$app/stores';

	import { getAgentById, getAgents, updateAgent, type AgentDefinition } from '$lib/apis/a2a';
	import AgentEditor from '$lib/components/workspace/Agents/AgentEditor.svelte';
	import AgentTestPanel from '$lib/components/workspace/Agents/AgentTestPanel.svelte';

	const i18n = getContext('i18n');

	let agent: AgentDefinition | null = null;
	let agentNames: Record<string, string> = {};
	let editorKey = 0;

	const load = async (id: string) => {
		agent = await getAgentById(localStorage.token, id).catch((e) => {
			toast.error(`${e}`);
			return null;
		});
		const agents = await getAgents(localStorage.token).catch(() => []);
		agentNames = Object.fromEntries(agents.map((a: AgentDefinition) => [a.id, a.name]));
	};

	onMount(async () => {
		const id = $page.url.searchParams.get('id');
		if (!id) return goto('/workspace/agents');
		await load(id);
		if (!agent) goto('/workspace/agents');
	});

	const onSubmit = async (form: AgentDefinition) => {
		if (!agent) return;
		const res = await updateAgent(localStorage.token, agent.id, form);
		if (res) {
			toast.success($i18n.t('Agent bijgewerkt'));
			agent = res;
			editorKey += 1;
		}
	};
</script>

{#if agent}
	{#key editorKey}
		<AgentEditor edit={true} {agent} {onSubmit} />
	{/key}
	<AgentTestPanel {agent} {agentNames} />
{/if}
