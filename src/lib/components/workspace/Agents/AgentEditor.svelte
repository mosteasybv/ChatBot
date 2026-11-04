<script lang="ts">
	import { getContext, onMount } from 'svelte';
	import { toast } from 'svelte-sonner';
	import { goto } from '$app/navigation';

	import { models, tools, knowledge as knowledgeCollections } from '$lib/stores';
	import { getTools } from '$lib/apis/tools';
	import { getKnowledgeBases } from '$lib/apis/knowledge';
	import { getAgents, getA2AStatus, type AgentDefinition } from '$lib/apis/a2a';

	import Checkbox from '$lib/components/common/Checkbox.svelte';
	import Switch from '$lib/components/common/Switch.svelte';
	import Textarea from '$lib/components/common/Textarea.svelte';
	import Tooltip from '$lib/components/common/Tooltip.svelte';
	import Plus from '$lib/components/icons/Plus.svelte';
	import XMark from '$lib/components/icons/XMark.svelte';
	import Clipboard from '$lib/components/icons/Clipboard.svelte';
	import { copyToClipboard } from '$lib/utils';

	const i18n = getContext('i18n');

	export let agent: AgentDefinition | null = null;
	export let edit = false;
	export let onSubmit: (agent: AgentDefinition) => Promise<void>;

	let loading = false;
	let loaded = false;

	let providers: string[] = [];
	let otherAgents: AgentDefinition[] = [];

	const emptyAgent = (): AgentDefinition => ({
		id: '',
		name: '',
		description: '',
		profileImageUrl: '/static/favicon.png',
		tags: [],
		enabled: true,
		source: { type: 'Workspace', model: '', provider: 'OpenAi', apiKeyEnv: '', baseUrl: '' },
		systemPrompt: '',
		params: { temperature: null, topP: null, maxTokens: null, seed: null },
		knowledgeIds: [],
		toolIds: [],
		features: { webSearch: false, imageGeneration: false, codeInterpreter: false },
		a2a: { peers: [], remoteAgents: [], expose: true, maxDelegationDepth: null, skills: [] }
	});

	let form: AgentDefinition = emptyAgent();
	let idTouched = false;
	let tagsText = '';

	// Modellen/GPT's uit WorkEasy; agents die zelf als verbinding zijn toegevoegd (a2a.*) laten we weg om lussen te voorkomen.
	let workspaceModels: any[] = [];
	$: workspaceModels = (($models ?? []) as any[]).filter(
		(m) => !m.id.startsWith('a2a.') && !(m?.info?.meta?.hidden ?? false) && m?.owned_by !== 'arena'
	);
	$: selectedModel = workspaceModels.find((m) => m.id === form.source.model);
	$: isWorkspace = form.source.type === 'Workspace';
	$: modelKnowledgeNames = (selectedModel?.info?.meta?.knowledge ?? [])
		.map((k: any) => k.name)
		.filter((n: string) => n)
		.join(', ');

	const slugify = (value: string) =>
		value
			.toLowerCase()
			.normalize('NFD')
			.replace(/[̀-ͯ]/g, '')
			.replace(/[^a-z0-9_-]+/g, '-')
			.replace(/^-+|-+$/g, '')
			.slice(0, 64);

	$: if (!edit && !idTouched) {
		form.id = slugify(form.name);
	}

	const numberOrNull = (value: any) =>
		value === '' || value === null || value === undefined || Number.isNaN(Number(value))
			? null
			: Number(value);

	const onModelSelected = () => {
		const model = workspaceModels.find((m) => m.id === form.source.model);
		if (!model) return;

		// Neem de standaardinstellingen van de GPT over, net als bij chatten in de werkruimte.
		const meta: any = model?.info?.meta ?? {};
		if (!form.name) form.name = model.name;
		if (!form.description && meta.description) form.description = meta.description;
		if (
			(!form.profileImageUrl || form.profileImageUrl === '/static/favicon.png') &&
			meta.profile_image_url
		) {
			form.profileImageUrl = meta.profile_image_url;
		}
		if (form.toolIds.length === 0 && (meta.toolIds ?? []).length > 0) {
			form.toolIds = [...meta.toolIds];
		}
	};

	const toggleInList = (list: string[], value: string, checked: boolean) =>
		checked ? Array.from(new Set([...list, value])) : list.filter((v) => v !== value);

	const addRemoteAgent = () => {
		form.a2a.remoteAgents = [
			...form.a2a.remoteAgents,
			{ name: '', url: '', description: '', apiKeyEnv: '' }
		];
	};

	const addSkill = () => {
		form.a2a.skills = [
			...form.a2a.skills,
			{ id: '', name: '', description: '', examples: [], tags: [] }
		];
	};

	const submitHandler = async () => {
		loading = true;

		const payload: AgentDefinition = {
			...form,
			tags: tagsText
				.split(',')
				.map((t) => t.trim())
				.filter((t) => t),
			params: {
				temperature: numberOrNull(form.params.temperature),
				topP: numberOrNull(form.params.topP),
				maxTokens: numberOrNull(form.params.maxTokens),
				seed: numberOrNull(form.params.seed)
			},
			source: isWorkspace
				? { type: 'Workspace', model: form.source.model }
				: {
						type: 'Provider',
						model: form.source.model,
						provider: form.source.provider,
						apiKeyEnv: form.source.apiKeyEnv || null,
						baseUrl: form.source.provider === 'Custom' ? form.source.baseUrl : null
					},
			knowledgeIds: isWorkspace ? form.knowledgeIds : [],
			toolIds: isWorkspace ? form.toolIds : [],
			features: isWorkspace
				? form.features
				: { webSearch: false, imageGeneration: false, codeInterpreter: false },
			a2a: {
				...form.a2a,
				maxDelegationDepth: numberOrNull(form.a2a.maxDelegationDepth),
				remoteAgents: form.a2a.remoteAgents
					.filter((r) => r.name.trim() || r.url.trim())
					.map((r) => ({ ...r, apiKeyEnv: r.apiKeyEnv || null })),
				skills: form.a2a.skills
					.filter((s) => s.name.trim())
					.map((s) => ({
						...s,
						examples: Array.isArray(s.examples)
							? s.examples
							: `${s.examples ?? ''}`
									.split('\n')
									.map((e) => e.trim())
									.filter((e) => e)
					}))
			}
		};

		try {
			await onSubmit(payload);
		} catch (e) {
			toast.error(`${e}`);
		}

		loading = false;
	};

	onMount(async () => {
		if (agent) {
			const base = emptyAgent();
			form = {
				...base,
				...agent,
				source: { ...base.source, ...agent.source },
				params: { ...base.params, ...agent.params },
				features: { ...base.features, ...agent.features },
				a2a: { ...base.a2a, ...agent.a2a }
			};
			tagsText = (agent.tags ?? []).join(', ');
			idTouched = true;
		}

		const [status, agents, toolList, knowledgeList] = await Promise.all([
			getA2AStatus(localStorage.token).catch((e) => {
				toast.error(`${e}`);
				return null;
			}),
			getAgents(localStorage.token).catch(() => []),
			getTools(localStorage.token).catch(() => []),
			getKnowledgeBases(localStorage.token).catch(() => [])
		]);

		providers = status?.providers ?? [];
		otherAgents = (agents ?? []).filter((a) => a.id !== form.id);
		tools.set(toolList);
		knowledgeCollections.set(knowledgeList);

		loaded = true;
	});
</script>

{#if loaded}
	<form
		class="flex flex-col max-w-3xl mx-auto mt-4 mb-10 gap-6 text-sm"
		on:submit|preventDefault={submitHandler}
	>
		<button
			class="flex space-x-1 w-fit text-gray-500"
			type="button"
			on:click={() => goto('/workspace/agents')}
		>
			<div class=" self-center">&larr;</div>
			<div class=" self-center font-medium">{$i18n.t('Back')}</div>
		</button>

		<!-- Identiteit -->
		<div class="flex gap-4 items-start">
			<img
				src={form.profileImageUrl || '/static/favicon.png'}
				alt="agent"
				class="rounded-xl size-20 object-cover shrink-0"
			/>
			<div class="flex flex-col flex-1 gap-1">
				<input
					class="text-3xl font-semibold w-full bg-transparent outline-hidden"
					placeholder={$i18n.t('Agent naam')}
					bind:value={form.name}
					required
				/>
				<input
					class="text-xs w-full bg-transparent text-gray-500 outline-hidden"
					placeholder={$i18n.t('Agent ID (bijv. juridisch-adviseur)')}
					bind:value={form.id}
					on:input={() => (idTouched = true)}
					disabled={edit}
					required
				/>
				<div class="flex items-center gap-2 mt-1">
					<Switch bind:state={form.enabled} />
					<span class="text-xs text-gray-500"
						>{form.enabled ? $i18n.t('Actief') : $i18n.t('Uitgeschakeld')}</span
					>
				</div>
			</div>
		</div>

		<div>
			<div class="font-semibold mb-1">{$i18n.t('Beschrijving')}</div>
			<div class="text-xs text-gray-500 mb-1">
				{$i18n.t(
					'Andere agents lezen deze beschrijving om te bepalen wanneer ze deze agent inschakelen. Beschrijf dus duidelijk waar hij goed in is.'
				)}
			</div>
			<Textarea
				className="w-full text-sm bg-transparent outline-hidden resize-none"
				placeholder={$i18n.t('Bijv. Beantwoordt vragen over omgevingswet en vergunningen.')}
				bind:value={form.description}
				rows={2}
			/>
		</div>

		<!-- Model -->
		<div class="flex flex-col gap-2">
			<div class="font-semibold">{$i18n.t('Model')}</div>
			<div class="flex gap-4 text-sm">
				<label class="flex items-center gap-1.5 cursor-pointer">
					<input type="radio" bind:group={form.source.type} value="Workspace" />
					{$i18n.t('GPT / model uit WorkEasy')}
				</label>
				<label class="flex items-center gap-1.5 cursor-pointer">
					<input type="radio" bind:group={form.source.type} value="Provider" />
					{$i18n.t('Directe provider (LlmTornado)')}
				</label>
			</div>

			{#if isWorkspace}
				<select
					class="w-full rounded-lg py-2 px-3 text-sm bg-gray-50 dark:bg-gray-850 outline-hidden"
					bind:value={form.source.model}
					on:change={onModelSelected}
					required
				>
					<option value="" disabled>{$i18n.t('Kies een GPT of model')}</option>
					{#each workspaceModels as model}
						<option value={model.id}
							>{model.name}{model?.info?.base_model_id ? ' (GPT)' : ''}</option
						>
					{/each}
				</select>
				<div class="text-xs text-gray-500">
					{$i18n.t(
						'De agent werkt precies zoals deze GPT in WorkEasy: systeemprompt, kennis, filters en parameters van de GPT worden automatisch toegepast. Hieronder kun je per agent nog extra instructies, kennis en tools toevoegen.'
					)}
					{#if modelKnowledgeNames}
						<div class="mt-1">
							{$i18n.t('Kennis van de GPT')}: {modelKnowledgeNames}
						</div>
					{/if}
				</div>
			{:else}
				<div class="grid grid-cols-1 md:grid-cols-2 gap-2">
					<div>
						<div class="text-xs text-gray-500 mb-0.5">{$i18n.t('Provider')}</div>
						<select
							class="w-full rounded-lg py-2 px-3 text-sm bg-gray-50 dark:bg-gray-850 outline-hidden"
							bind:value={form.source.provider}
							required
						>
							{#each providers as provider}
								<option value={provider}>{provider}</option>
							{/each}
						</select>
					</div>
					<div>
						<div class="text-xs text-gray-500 mb-0.5">{$i18n.t('Modelnaam bij provider')}</div>
						<input
							class="w-full rounded-lg py-2 px-3 text-sm bg-gray-50 dark:bg-gray-850 outline-hidden"
							placeholder="gpt-4o, claude-sonnet-4-5, mistral-large-latest …"
							bind:value={form.source.model}
							required
						/>
					</div>
					<div>
						<div class="text-xs text-gray-500 mb-0.5">
							{$i18n.t('Omgevingsvariabele met API-sleutel')}
						</div>
						<input
							class="w-full rounded-lg py-2 px-3 text-sm bg-gray-50 dark:bg-gray-850 outline-hidden"
							placeholder="OPENAI_API_KEY"
							bind:value={form.source.apiKeyEnv}
						/>
					</div>
					{#if form.source.provider === 'Custom'}
						<div>
							<div class="text-xs text-gray-500 mb-0.5">
								{$i18n.t('Basis-URL (OpenAI-compatibel)')}
							</div>
							<input
								class="w-full rounded-lg py-2 px-3 text-sm bg-gray-50 dark:bg-gray-850 outline-hidden"
								placeholder="http://ollama:11434"
								bind:value={form.source.baseUrl}
								required
							/>
						</div>
					{/if}
				</div>
				<div class="text-xs text-gray-500">
					{$i18n.t(
						'De sleutel zelf wordt nooit opgeslagen; zet hem als omgevingsvariabele op de agentservice. Kennis, tools en webzoeken van WorkEasy zijn alleen beschikbaar met een WorkEasy-GPT als bron.'
					)}
				</div>
			{/if}
		</div>

		<!-- Instructies -->
		<div>
			<div class="font-semibold mb-1">{$i18n.t('Extra instructies (systeemprompt)')}</div>
			<Textarea
				className="w-full text-sm bg-gray-50 dark:bg-gray-850 rounded-lg p-3 outline-hidden resize-none"
				placeholder={$i18n.t(
					'Bijv. Je bent de intake-agent. Vat de vraag samen en schakel specialisten in.'
				)}
				bind:value={form.systemPrompt}
				rows={5}
			/>
		</div>

		<!-- Parameters -->
		<div>
			<div class="font-semibold mb-1">{$i18n.t('Parameters')}</div>
			<div class="grid grid-cols-2 md:grid-cols-4 gap-2">
				<label class="flex flex-col text-xs text-gray-500 gap-0.5">
					{$i18n.t('Temperatuur')}
					<input
						class="rounded-lg py-1.5 px-2 text-sm bg-gray-50 dark:bg-gray-850 outline-hidden"
						type="number"
						step="0.05"
						min="0"
						max="2"
						placeholder={$i18n.t('Standaard')}
						bind:value={form.params.temperature}
					/>
				</label>
				<label class="flex flex-col text-xs text-gray-500 gap-0.5">
					Top P
					<input
						class="rounded-lg py-1.5 px-2 text-sm bg-gray-50 dark:bg-gray-850 outline-hidden"
						type="number"
						step="0.05"
						min="0"
						max="1"
						placeholder={$i18n.t('Standaard')}
						bind:value={form.params.topP}
					/>
				</label>
				<label class="flex flex-col text-xs text-gray-500 gap-0.5">
					{$i18n.t('Max. tokens')}
					<input
						class="rounded-lg py-1.5 px-2 text-sm bg-gray-50 dark:bg-gray-850 outline-hidden"
						type="number"
						min="1"
						placeholder={$i18n.t('Standaard')}
						bind:value={form.params.maxTokens}
					/>
				</label>
				<label class="flex flex-col text-xs text-gray-500 gap-0.5">
					Seed
					<input
						class="rounded-lg py-1.5 px-2 text-sm bg-gray-50 dark:bg-gray-850 outline-hidden"
						type="number"
						placeholder={$i18n.t('Standaard')}
						bind:value={form.params.seed}
					/>
				</label>
			</div>
		</div>

		{#if isWorkspace}
			<!-- Kennis -->
			<div>
				<div class="font-semibold mb-1">{$i18n.t('Kennis')}</div>
				<div class="text-xs text-gray-500 mb-1">
					{$i18n.t('Extra kennisbanken die deze agent bij elke vraag doorzoekt.')}
				</div>
				{#if ($knowledgeCollections ?? []).length === 0}
					<div class="text-xs text-gray-500">
						{$i18n.t('To attach knowledge base here, add them to the "Knowledge" workspace first.')}
					</div>
				{:else}
					<div class="flex flex-wrap gap-x-4 gap-y-1">
						{#each $knowledgeCollections ?? [] as kb (kb['id'])}
							<label class="flex items-center gap-2">
								<Checkbox
									state={form.knowledgeIds.includes(kb['id']) ? 'checked' : 'unchecked'}
									on:change={(e) => {
										form.knowledgeIds = toggleInList(
											form.knowledgeIds,
											kb['id'],
											e.detail === 'checked'
										);
									}}
								/>
								<span>{kb['name']}</span>
							</label>
						{/each}
					</div>
				{/if}
			</div>

			<!-- Tools -->
			<div>
				<div class="font-semibold mb-1">{$i18n.t('Tools')}</div>
				{#if ($tools ?? []).length === 0}
					<div class="text-xs text-gray-500">
						{$i18n.t('To select toolkits here, add them to the "Tools" workspace first.')}
					</div>
				{:else}
					<div class="flex flex-wrap gap-x-4 gap-y-1">
						{#each $tools ?? [] as tool (tool['id'])}
							<label class="flex items-center gap-2">
								<Checkbox
									state={form.toolIds.includes(tool['id']) ? 'checked' : 'unchecked'}
									on:change={(e) => {
										form.toolIds = toggleInList(form.toolIds, tool['id'], e.detail === 'checked');
									}}
								/>
								<span class="capitalize">{tool['name']}</span>
							</label>
						{/each}
					</div>
				{/if}
			</div>

			<!-- Functies -->
			<div>
				<div class="font-semibold mb-1">{$i18n.t('Functies')}</div>
				<div class="flex flex-wrap gap-x-4 gap-y-1">
					<label class="flex items-center gap-2">
						<Checkbox
							state={form.features.webSearch ? 'checked' : 'unchecked'}
							on:change={(e) => (form.features.webSearch = e.detail === 'checked')}
						/>
						{$i18n.t('Web Search')}
					</label>
					<label class="flex items-center gap-2">
						<Checkbox
							state={form.features.imageGeneration ? 'checked' : 'unchecked'}
							on:change={(e) => (form.features.imageGeneration = e.detail === 'checked')}
						/>
						{$i18n.t('Image Generation')}
					</label>
					<label class="flex items-center gap-2">
						<Checkbox
							state={form.features.codeInterpreter ? 'checked' : 'unchecked'}
							on:change={(e) => (form.features.codeInterpreter = e.detail === 'checked')}
						/>
						{$i18n.t('Code Interpreter')}
					</label>
				</div>
			</div>
		{/if}

		<!-- A2A -->
		<div class="flex flex-col gap-3 rounded-xl border border-gray-100 dark:border-gray-850 p-4">
			<div>
				<div class="font-semibold">{$i18n.t('Samenwerken met andere agents (A2A)')}</div>
				<div class="text-xs text-gray-500">
					{$i18n.t(
						'Deze agent kan vragen voorleggen aan de agents die je hier aanvinkt. Elke collega verschijnt voor het model als een tool; het gesprek tussen de agents verloopt via het A2A-protocol.'
					)}
				</div>
			</div>

			<div>
				<div class="text-xs font-medium mb-1">{$i18n.t('Mag praten met')}</div>
				{#if otherAgents.length === 0}
					<div class="text-xs text-gray-500">
						{$i18n.t('Er zijn nog geen andere agents. Maak er eerst nog een aan.')}
					</div>
				{:else}
					<div class="flex flex-col gap-1">
						{#each otherAgents as other}
							<label class="flex items-center gap-2">
								<Checkbox
									state={form.a2a.peers.includes(other.id) ? 'checked' : 'unchecked'}
									on:change={(e) => {
										form.a2a.peers = toggleInList(form.a2a.peers, other.id, e.detail === 'checked');
									}}
								/>
								<span class="font-medium">{other.name}</span>
								<span class="text-xs text-gray-500 line-clamp-1">{other.description}</span>
								{#if !other.enabled || !other.a2a?.expose}
									<span class="text-xs text-amber-600">({$i18n.t('niet beschikbaar')})</span>
								{/if}
							</label>
						{/each}
					</div>
				{/if}
			</div>

			<div>
				<div class="flex items-center justify-between">
					<div class="text-xs font-medium">{$i18n.t('Externe A2A-agents')}</div>
					<button
						class="p-1 rounded-lg hover:bg-black/5 dark:hover:bg-white/5"
						type="button"
						on:click={addRemoteAgent}
					>
						<Plus className="size-3.5" />
					</button>
				</div>
				{#each form.a2a.remoteAgents as remote, idx}
					<div class="grid grid-cols-1 md:grid-cols-[1fr_2fr_auto] gap-1.5 mt-1.5 items-start">
						<input
							class="rounded-lg py-1.5 px-2 text-sm bg-gray-50 dark:bg-gray-850 outline-hidden"
							placeholder={$i18n.t('Korte naam')}
							bind:value={remote.name}
						/>
						<input
							class="rounded-lg py-1.5 px-2 text-sm bg-gray-50 dark:bg-gray-850 outline-hidden"
							placeholder="https://andere-server/a2a/agent"
							bind:value={remote.url}
						/>
						<button
							class="p-1.5 rounded-lg hover:bg-black/5 dark:hover:bg-white/5"
							type="button"
							on:click={() => {
								form.a2a.remoteAgents = form.a2a.remoteAgents.filter((_, i) => i !== idx);
							}}
						>
							<XMark className="size-4" />
						</button>
						<input
							class="rounded-lg py-1.5 px-2 text-sm bg-gray-50 dark:bg-gray-850 outline-hidden md:col-span-2"
							placeholder={$i18n.t('Waar is deze agent goed in?')}
							bind:value={remote.description}
						/>
						<span />
						<input
							class="rounded-lg py-1.5 px-2 text-sm bg-gray-50 dark:bg-gray-850 outline-hidden md:col-span-2"
							placeholder={$i18n.t('Omgevingsvariabele met token (optioneel)')}
							bind:value={remote.apiKeyEnv}
						/>
					</div>
				{/each}
			</div>

			<div class="flex flex-wrap gap-6">
				<label class="flex items-center gap-2">
					<Switch bind:state={form.a2a.expose} />
					<span class="text-xs">{$i18n.t('Beschikbaar voor andere agents en A2A-clients')}</span>
				</label>
				<label class="flex items-center gap-2 text-xs">
					{$i18n.t('Max. doorverwijsdiepte')}
					<input
						class="w-16 rounded-lg py-1 px-2 text-sm bg-gray-50 dark:bg-gray-850 outline-hidden"
						type="number"
						min="0"
						max="10"
						placeholder="3"
						bind:value={form.a2a.maxDelegationDepth}
					/>
				</label>
			</div>

			<div>
				<div class="flex items-center justify-between">
					<div class="text-xs font-medium">
						{$i18n.t('Vaardigheden (gepubliceerd in de agent card)')}
					</div>
					<button
						class="p-1 rounded-lg hover:bg-black/5 dark:hover:bg-white/5"
						type="button"
						on:click={addSkill}
					>
						<Plus className="size-3.5" />
					</button>
				</div>
				{#each form.a2a.skills as skill, idx}
					<div class="grid grid-cols-1 md:grid-cols-[1fr_2fr_auto] gap-1.5 mt-1.5 items-start">
						<input
							class="rounded-lg py-1.5 px-2 text-sm bg-gray-50 dark:bg-gray-850 outline-hidden"
							placeholder={$i18n.t('Naam')}
							bind:value={skill.name}
						/>
						<input
							class="rounded-lg py-1.5 px-2 text-sm bg-gray-50 dark:bg-gray-850 outline-hidden"
							placeholder={$i18n.t('Beschrijving')}
							bind:value={skill.description}
						/>
						<button
							class="p-1.5 rounded-lg hover:bg-black/5 dark:hover:bg-white/5"
							type="button"
							on:click={() => {
								form.a2a.skills = form.a2a.skills.filter((_, i) => i !== idx);
							}}
						>
							<XMark className="size-4" />
						</button>
					</div>
				{/each}
			</div>
		</div>

		<div>
			<div class="font-semibold mb-1">{$i18n.t('Tags')}</div>
			<input
				class="w-full rounded-lg py-2 px-3 text-sm bg-gray-50 dark:bg-gray-850 outline-hidden"
				placeholder={$i18n.t('Komma-gescheiden, bijv. juridisch, intake')}
				bind:value={tagsText}
			/>
		</div>

		{#if edit && agent?.endpoints}
			<div class="flex flex-col gap-1 text-xs">
				<div class="font-semibold text-sm">{$i18n.t('Endpoints')}</div>
				{#each [['A2A (JSON-RPC)', agent.endpoints.a2a], ['Agent card', agent.endpoints.card], [$i18n.t('Model-id (OpenAI-verbinding)'), agent.endpoints.openAiModel]] as [label, value]}
					<div class="flex items-center gap-2">
						<span class="w-44 text-gray-500 shrink-0">{label}</span>
						<code class="truncate">{value}</code>
						<Tooltip content={$i18n.t('Copy')}>
							<button
								type="button"
								class="p-1 rounded hover:bg-black/5 dark:hover:bg-white/5"
								on:click={async () => {
									if (await copyToClipboard(value)) toast.success($i18n.t('Copied to clipboard'));
								}}
							>
								<Clipboard className="size-3.5" />
							</button>
						</Tooltip>
					</div>
				{/each}
			</div>
		{/if}

		<button
			class="text-sm px-3 py-2 transition rounded-lg bg-black hover:bg-gray-900 text-white dark:bg-white dark:hover:bg-gray-100 dark:text-black flex w-full justify-center {loading
				? 'cursor-not-allowed opacity-70'
				: ''}"
			type="submit"
			disabled={loading}
		>
			{edit ? $i18n.t('Save & Update') : $i18n.t('Save & Create')}
		</button>
	</form>
{/if}
