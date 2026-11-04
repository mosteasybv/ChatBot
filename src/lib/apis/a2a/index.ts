import { WEBUI_API_BASE_URL } from '$lib/constants';

// Client voor de A2A-agents (LlmTornado-service). Alle verzoeken lopen via de WorkEasy-backend
// (/api/v1/a2a), die rechten controleert en doorstuurt naar de agentservice.

export type AgentSourceType = 'Workspace' | 'Provider';

export interface AgentDefinition {
	id: string;
	name: string;
	description: string;
	profileImageUrl?: string;
	tags: string[];
	enabled: boolean;
	source: {
		type: AgentSourceType;
		model: string;
		provider?: string | null;
		apiKeyEnv?: string | null;
		baseUrl?: string | null;
	};
	systemPrompt: string;
	params: {
		temperature?: number | null;
		topP?: number | null;
		maxTokens?: number | null;
		seed?: number | null;
	};
	knowledgeIds: string[];
	toolIds: string[];
	features: { webSearch: boolean; imageGeneration: boolean; codeInterpreter: boolean };
	a2a: {
		peers: string[];
		remoteAgents: { name: string; url: string; description: string; apiKeyEnv?: string | null }[];
		expose: boolean;
		maxDelegationDepth?: number | null;
		skills: { id: string; name: string; description: string; examples: string[]; tags: string[] }[];
	};
	createdBy?: string | null;
	createdAt?: number;
	updatedAt?: number;
	endpoints?: { a2a: string; card: string; openAiModel: string };
}

export interface TraceEvent {
	timestamp: string;
	type: 'request' | 'delegation' | 'delegation_result' | 'response' | 'error';
	from: string;
	to: string;
	content: string;
	depth: number;
}

const request = async (token: string, path: string, method = 'GET', body?: object) => {
	let error = null;

	const res = await fetch(`${WEBUI_API_BASE_URL}/a2a${path}`, {
		method,
		headers: {
			Accept: 'application/json',
			'Content-Type': 'application/json',
			authorization: `Bearer ${token}`
		},
		...(body ? { body: JSON.stringify(body) } : {})
	})
		.then(async (res) => {
			if (!res.ok) throw await res.json();
			return res.json();
		})
		.catch((err) => {
			error = err?.detail ?? err;
			console.log(err);
			return null;
		});

	if (error) {
		throw error;
	}

	return res;
};

export const getA2AConfig = async (token: string) => request(token, '/config');

export const getA2AStatus = async (token: string) => request(token, '/status');

export const getAgents = async (token: string): Promise<AgentDefinition[]> =>
	request(token, '/agents');

export const getAgentById = async (token: string, id: string): Promise<AgentDefinition> =>
	request(token, `/agents/${encodeURIComponent(id)}`);

export const createAgent = async (token: string, agent: AgentDefinition) =>
	request(token, '/agents/create', 'POST', agent);

export const updateAgent = async (token: string, id: string, agent: AgentDefinition) =>
	request(token, `/agents/${encodeURIComponent(id)}/update`, 'POST', agent);

export const deleteAgent = async (token: string, id: string) =>
	request(token, `/agents/${encodeURIComponent(id)}/delete`, 'DELETE');

export const chatWithAgent = async (
	token: string,
	id: string,
	messages: { role: string; content: string }[]
): Promise<{ content: string; traceId: string; trace: { events: TraceEvent[] } }> =>
	request(token, `/agents/${encodeURIComponent(id)}/chat`, 'POST', { messages });

export const getAgentNetwork = async (token: string) => request(token, '/network');

export const getTrace = async (token: string, traceId: string) =>
	request(token, `/traces/${encodeURIComponent(traceId)}`);
