<script lang="ts">
	import { getContext, onMount } from 'svelte';
	import { toast } from 'svelte-sonner';
	import { marked } from 'marked';

	import { WEBUI_NAME, user } from '$lib/stores';
	import { deleteAgent, getA2AConfig, getAgents, type AgentDefinition } from '$lib/apis/a2a';

	import ConfirmDialog from '$lib/components/common/ConfirmDialog.svelte';
	import Tooltip from '$lib/components/common/Tooltip.svelte';
	import Search from '$lib/components/icons/Search.svelte';
	import Plus from '$lib/components/icons/Plus.svelte';
	import Pencil from '$lib/components/icons/Pencil.svelte';
	import GarbageBin from '$lib/components/icons/GarbageBin.svelte';

	const i18n = getContext('i18n');

	let loaded = false;
	let enabled = true;
	let error = '';
	let agents: AgentDefinition[] = [];
	let searchValue = '';

	let showDeleteConfirm = false;
	let selectedAgent: AgentDefinition | null = null;

	$: names = Object.fromEntries(agents.map((a) => [a.id, a.name]));
	$: filteredAgents = agents.filter((a) =>
		`${a.name} ${a.id} ${a.description} ${(a.tags ?? []).join(' ')}`
			.toLowerCase()
			.includes(searchValue.toLowerCase())
	);
	$: connections = agents.flatMap((a) => [
		...(a.a2a?.peers ?? []).map((p) => ({ from: a.id, to: names[p] ?? p })),
		...(a.a2a?.remoteAgents ?? []).map((r) => ({ from: a.id, to: `${r.name} (extern)` }))
	]);

	const canEdit = (agent: AgentDefinition) =>
		$user?.role === 'admin' || !agent.createdBy || agent.createdBy === $user?.id;

	const load = async () => {
		try {
			agents = await getAgents(localStorage.token);
			error = '';
		} catch (e) {
			error = `${e}`;
		}
	};

	const deleteHandler = async (agent: AgentDefinition) => {
		const res = await deleteAgent(localStorage.token, agent.id).catch((e) => {
			toast.error(`${e}`);
			return null;
		});
		if (res) {
			toast.success($i18n.t('Agent verwijderd'));
			await load();
		}
	};

	onMount(async () => {
		const config = await getA2AConfig(localStorage.token).catch(() => ({ enabled: false }));
		enabled = config?.enabled ?? false;
		if (enabled) await load();
		loaded = true;
	});
</script>

<svelte:head>
	<title>
		{$i18n.t('Agents')} | {$WEBUI_NAME}
	</title>
</svelte:head>

{#if loaded}
	<ConfirmDialog
		bind:show={showDeleteConfirm}
		title={$i18n.t('Agent verwijderen?')}
		message={$i18n.t(
			'De agent wordt ook verwijderd uit de lijst van agents die met hem mogen praten.'
		)}
		on:confirm={() => selectedAgent && deleteHandler(selectedAgent)}
	/>

	<div class="flex flex-col gap-1 my-1.5">
		<div class="flex justify-between items-center">
			<div class="flex items-center md:self-center text-xl font-medium px-0.5">
				{$i18n.t('Agents')}
				<div class="flex self-center w-[1px] h-6 mx-2.5 bg-gray-50 dark:bg-gray-850" />
				<span class="text-lg font-medium text-gray-500 dark:text-gray-300"
					>{filteredAgents.length}</span
				>
			</div>
		</div>

		{#if enabled}
			<div class="flex flex-1 items-center w-full space-x-2">
				<div class="flex flex-1 items-center">
					<div class="self-center ml-1 mr-3">
						<Search className="size-3.5" />
					</div>
					<input
						class="w-full text-sm py-1 rounded-r-xl outline-hidden bg-transparent"
						bind:value={searchValue}
						placeholder={$i18n.t('Zoek agents')}
					/>
				</div>
				<a
					class="px-2 py-2 rounded-xl hover:bg-gray-700/10 dark:hover:bg-gray-100/10 dark:text-gray-300 dark:hover:text-white transition font-medium text-sm flex items-center space-x-1"
					href="/workspace/agents/create"
				>
					<Plus className="size-3.5" />
				</a>
			</div>
		{/if}
	</div>

	{#if !enabled}
		<div class="my-8 text-sm text-gray-500 max-w-xl">
			<div class="font-semibold text-gray-800 dark:text-gray-200 mb-1">
				{$i18n.t('De A2A-agentservice is nog niet gekoppeld')}
			</div>
			{$i18n.t(
				'Start de LlmTornado A2A-service (map a2a-agents) en zet op de WorkEasy-backend de omgevingsvariabelen A2A_AGENTS_BASE_URL en A2A_AGENTS_API_KEY.'
			)}
		</div>
	{:else if error}
		<div class="my-4 text-sm text-red-600">{error}</div>
	{:else}
		{#if connections.length > 0}
			<div class="my-2 text-xs text-gray-500 flex flex-wrap gap-1.5">
				<span class="font-medium mr-1">{$i18n.t('Wie praat met wie')}:</span>
				{#each connections as c}
					<span class="rounded-full bg-gray-50 dark:bg-gray-850 px-2 py-0.5"
						>{names[c.from] ?? c.from} → {c.to}</span
					>
				{/each}
			</div>
		{/if}

		<div class="my-2 mb-5 gap-2 grid lg:grid-cols-2 xl:grid-cols-3">
			{#each filteredAgents as agent (agent.id)}
				<div
					class="flex flex-col w-full px-3 py-2 dark:hover:bg-white/5 hover:bg-black/5 rounded-xl transition"
				>
					<div class="flex gap-4 mt-0.5 mb-0.5">
						<div class="w-[44px] shrink-0">
							<img
								src={agent.profileImageUrl || '/static/favicon.png'}
								alt="agent"
								class="rounded-full w-full h-auto object-cover {agent.enabled ? '' : 'opacity-50'}"
							/>
						</div>
						<a
							class="flex flex-1 w-full"
							href={`/workspace/agents/edit?id=${encodeURIComponent(agent.id)}`}
						>
							<div class="flex-1 self-center {agent.enabled ? '' : 'text-gray-500'}">
								<Tooltip
									content={marked.parse(agent.description || agent.id)}
									className="w-fit"
									placement="top-start"
								>
									<div class="font-semibold line-clamp-1">{agent.name}</div>
								</Tooltip>
								<div class="text-xs line-clamp-1">{agent.description || agent.id}</div>
							</div>
						</a>
					</div>

					<div class="flex justify-between items-center -mb-0.5 px-0.5">
						<div class="text-xs text-gray-500 flex gap-2 flex-wrap">
							<span>
								{agent.source.type === 'Workspace'
									? `GPT: ${agent.source.model}`
									: `${agent.source.provider}: ${agent.source.model}`}
							</span>
							{#if (agent.a2a?.peers?.length ?? 0) + (agent.a2a?.remoteAgents?.length ?? 0) > 0}
								<span>
									· {$i18n.t('{{count}} collega-agent(s)', {
										count: (agent.a2a?.peers?.length ?? 0) + (agent.a2a?.remoteAgents?.length ?? 0)
									})}
								</span>
							{/if}
							{#if !agent.enabled}
								<span>· {$i18n.t('uitgeschakeld')}</span>
							{/if}
						</div>

						{#if canEdit(agent)}
							<div class="flex flex-row gap-0.5 items-center">
								<a
									class="self-center w-fit text-sm px-2 py-2 dark:text-gray-300 dark:hover:text-white hover:bg-black/5 dark:hover:bg-white/5 rounded-xl"
									href={`/workspace/agents/edit?id=${encodeURIComponent(agent.id)}`}
								>
									<Pencil />
								</a>
								<button
									class="self-center w-fit text-sm px-2 py-2 dark:text-gray-300 dark:hover:text-white hover:bg-black/5 dark:hover:bg-white/5 rounded-xl"
									type="button"
									on:click={() => {
										selectedAgent = agent;
										showDeleteConfirm = true;
									}}
								>
									<GarbageBin />
								</button>
							</div>
						{/if}
					</div>
				</div>
			{/each}
		</div>

		{#if agents.length === 0}
			<div class="my-6 text-sm text-gray-500 max-w-xl">
				{$i18n.t(
					'Nog geen agents. Maak een agent op basis van een GPT uit de werkruimte en geef aan met welke andere agents hij mag overleggen.'
				)}
			</div>
		{/if}
	{/if}
{/if}
