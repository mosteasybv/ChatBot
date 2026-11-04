"""
A2A-agents (LlmTornado)

Proxy tussen de WorkEasy-frontend en de A2A-agentservice (map ``a2a-agents``). De frontend praat
alleen met WorkEasy; deze router controleert de gebruiker en zijn rechten en stuurt het verzoek door
met de service-sleutel (``A2A_AGENTS_API_KEY``).
"""

import logging
from typing import Optional

import aiohttp
from fastapi import APIRouter, Depends, HTTPException, Request, status
from fastapi.responses import JSONResponse

from open_webui.env import (
    A2A_AGENTS_API_KEY,
    A2A_AGENTS_BASE_URL,
    A2A_AGENTS_TIMEOUT,
    SRC_LOG_LEVELS,
)
from open_webui.models.knowledge import Knowledges
from open_webui.models.models import Models
from open_webui.models.tools import Tools
from open_webui.utils.access_control import has_access, has_permission
from open_webui.utils.auth import get_verified_user

log = logging.getLogger(__name__)
log.setLevel(SRC_LOG_LEVELS["MAIN"])

router = APIRouter()


############################
# Helpers
############################


def _check_permission(request: Request, user):
    """Agents beheren mag een beheerder of een gebruiker die ook werkruimte-modellen mag beheren."""
    if user.role == "admin":
        return
    if not has_permission(
        user.id, "workspace.models", request.app.state.config.USER_PERMISSIONS
    ):
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail="Je hebt geen rechten om A2A-agents te beheren.",
        )


async def _call(
    method: str,
    path: str,
    user,
    payload: Optional[dict] = None,
    timeout: Optional[int] = None,
):
    if not A2A_AGENTS_BASE_URL:
        raise HTTPException(
            status_code=status.HTTP_503_SERVICE_UNAVAILABLE,
            detail="De A2A-agentservice is niet geconfigureerd (zet A2A_AGENTS_BASE_URL).",
        )

    headers = {"X-WorkEasy-User": user.id}
    if A2A_AGENTS_API_KEY:
        headers["Authorization"] = f"Bearer {A2A_AGENTS_API_KEY}"

    url = f"{A2A_AGENTS_BASE_URL.rstrip('/')}/api{path}"
    try:
        async with aiohttp.ClientSession(
            timeout=aiohttp.ClientTimeout(total=timeout or A2A_AGENTS_TIMEOUT),
            trust_env=True,
        ) as session:
            async with session.request(
                method, url, json=payload, headers=headers
            ) as response:
                try:
                    data = await response.json(content_type=None)
                except Exception:
                    data = {"detail": await response.text()}

                if response.status >= 400:
                    detail = (
                        data.get("detail") if isinstance(data, dict) else None
                    ) or f"A2A-agentservice gaf status {response.status}"
                    raise HTTPException(status_code=response.status, detail=detail)
                return data
    except HTTPException:
        raise
    except Exception as e:
        log.exception(e)
        raise HTTPException(
            status_code=status.HTTP_502_BAD_GATEWAY,
            detail=f"A2A-agentservice niet bereikbaar: {e}",
        )


def _can_read(item, user) -> bool:
    if item is None:
        return False
    return (
        user.role == "admin"
        or item.user_id == user.id
        or has_access(user.id, "read", item.access_control)
    )


def _validate_resources(form_data: dict, user):
    """
    De agentservice roept WorkEasy aan met een service-sleutel. Controleer daarom hier dat de gebruiker
    zelf toegang heeft tot de GPT, kennisbanken en tools die hij aan een agent koppelt.
    """
    if user.role == "admin":
        return

    source = form_data.get("source") or {}
    if (source.get("type") or "Workspace").lower() == "workspace":
        model_id = source.get("model")
        model = Models.get_model_by_id(model_id) if model_id else None
        # Basismodellen staan niet in de tabel; werkruimte-GPT's wel en hebben toegangsbeheer.
        if model is not None and not _can_read(model, user):
            raise HTTPException(
                status_code=status.HTTP_403_FORBIDDEN,
                detail=f"Geen toegang tot model '{model_id}'.",
            )

    for knowledge_id in form_data.get("knowledgeIds") or []:
        if not _can_read(Knowledges.get_knowledge_by_id(knowledge_id), user):
            raise HTTPException(
                status_code=status.HTTP_403_FORBIDDEN,
                detail=f"Geen toegang tot kennisbank '{knowledge_id}'.",
            )

    for tool_id in form_data.get("toolIds") or []:
        if not _can_read(Tools.get_tool_by_id(tool_id), user):
            raise HTTPException(
                status_code=status.HTTP_403_FORBIDDEN,
                detail=f"Geen toegang tot tool '{tool_id}'.",
            )


async def _check_owner(agent_id: str, user):
    agent = await _call("GET", f"/agents/{agent_id}", user)
    if user.role != "admin" and agent.get("createdBy") not in (None, user.id):
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail="Alleen de maker of een beheerder kan deze agent wijzigen.",
        )
    return agent


############################
# Endpoints
############################


@router.get("/config")
async def get_config(user=Depends(get_verified_user)):
    return {"enabled": bool(A2A_AGENTS_BASE_URL)}


@router.get("/status")
async def get_status(request: Request, user=Depends(get_verified_user)):
    _check_permission(request, user)
    return await _call("GET", "/status", user)


@router.get("/agents")
async def get_agents(request: Request, user=Depends(get_verified_user)):
    _check_permission(request, user)
    return await _call("GET", "/agents", user)


@router.get("/network")
async def get_network(request: Request, user=Depends(get_verified_user)):
    _check_permission(request, user)
    return await _call("GET", "/network", user)


@router.post("/agents/create")
async def create_agent(
    request: Request, form_data: dict, user=Depends(get_verified_user)
):
    _check_permission(request, user)
    _validate_resources(form_data, user)
    return await _call("POST", "/agents", user, form_data)


@router.get("/agents/{agent_id}")
async def get_agent(agent_id: str, request: Request, user=Depends(get_verified_user)):
    _check_permission(request, user)
    return await _call("GET", f"/agents/{agent_id}", user)


@router.post("/agents/{agent_id}/update")
async def update_agent(
    agent_id: str, request: Request, form_data: dict, user=Depends(get_verified_user)
):
    _check_permission(request, user)
    await _check_owner(agent_id, user)
    _validate_resources(form_data, user)
    return await _call("PUT", f"/agents/{agent_id}", user, form_data)


@router.delete("/agents/{agent_id}/delete")
async def delete_agent(
    agent_id: str, request: Request, user=Depends(get_verified_user)
):
    _check_permission(request, user)
    await _check_owner(agent_id, user)
    return await _call("DELETE", f"/agents/{agent_id}", user)


@router.post("/agents/{agent_id}/chat")
async def chat_with_agent(
    agent_id: str, request: Request, form_data: dict, user=Depends(get_verified_user)
):
    _check_permission(request, user)
    data = await _call(
        "POST",
        f"/agents/{agent_id}/chat",
        user,
        {"messages": form_data.get("messages", [])},
    )
    return JSONResponse(content=data)


@router.get("/traces/{trace_id}")
async def get_trace(trace_id: str, request: Request, user=Depends(get_verified_user)):
    _check_permission(request, user)
    return await _call("GET", f"/traces/{trace_id}", user)
